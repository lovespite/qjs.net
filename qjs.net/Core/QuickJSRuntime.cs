using QuickJsNet.Utils;
using QuickJSNet.Bindings;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text;

namespace QuickJsNet.Core;

/// <summary>
/// High-level QuickJS JavaScript runtime wrapper for .NET.
/// Manages a JSRuntime and JSContext, and provides methods for evaluation,
/// value manipulation, and native function registration.
/// <para>
/// This type is exposed publicly to allow source-generated
/// <see cref="QuickJsNet.Interop.IJSBinder"/> implementations to interact with
/// the engine. Most application code should use the higher-level
/// <see cref="QuickJSEngine"/> façade instead.
/// </para>
/// </summary>
public partial class QuickJSRuntime
{
    private static readonly string[] LOG_LEVELS = { "DEBUG", "WARN", "ERROR", "CRITICAL", "FATAL", "UNKNOWN1" };
    private IntPtr _runtime;
    private IntPtr _context;
    private bool _disposed;

    // Must keep references to prevent GC collection
    private readonly QuickJSNative.LogCallback _logCallbackDelegate;
    private readonly List<GCHandle> _pinnedDelegates = [];
    private readonly EventLoop _eventLoop;
    private readonly List<long> _wrappedObjectIds = new();

    // Process-wide map: native context pointer → managed runtime instance.
    // Allows AOT-safe static [UnmanagedCallersOnly] callbacks to resolve
    // their owning runtime from the ctx parameter without any reflection or
    // GCHandle allocation per call.
    private static readonly ConcurrentDictionary<IntPtr, QuickJSRuntime> _runtimesByContext = new();

    public IntPtr Context => _context;

    /// <summary>
    /// Resolve a managed <see cref="QuickJSRuntime"/> from a native context
    /// pointer. Used by source-generated JS callbacks to bridge back into
    /// managed land. AOT-safe.
    /// </summary>
    public static QuickJSRuntime? FromContext(IntPtr ctx)
        => _runtimesByContext.TryGetValue(ctx, out var rt) ? rt : null;

    /// <summary>Track an async op via the event loop. Used by interop bridges in this assembly.</summary>
    internal void TrackAsyncOpForBridge() => _eventLoop.TrackAsyncOp();

    /// <summary>
    /// Process all currently pending timers, queued callbacks and microtasks in a single pass.
    /// Must be called on the JS thread. Non-blocking; returns immediately when the queue is drained.
    /// Intended for host integrations that drive their own message loop (e.g. UI frame ticks)
    /// and need <c>setTimeout</c> / <c>setInterval</c> callbacks to fire while the host is idle.
    /// </summary>
    public void PumpEventLoop() => _eventLoop.DrainQueue();

    public event Action<int, string>? OnLog;

    public QuickJSRuntime(ulong memoryLimit = 0, ulong stackSize = 0)
    {
        _runtime = QuickJSNative.QJS_NewRuntime();
        if (_runtime == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create QuickJS runtime");

        if (memoryLimit > 0)
            QuickJSNative.QJS_SetMemoryLimit(_runtime, new UIntPtr(memoryLimit));
        if (stackSize > 0)
            QuickJSNative.QJS_SetMaxStackSize(_runtime, new UIntPtr(stackSize));

        QuickJSNative.QJS_StdInitHandlers(_runtime);

        _context = QuickJSNative.QJS_NewContext(_runtime);
        if (_context == IntPtr.Zero)
            throw new InvalidOperationException("Failed to create QuickJS context");

        // Set up log callback
        _logCallbackDelegate = LogCallbackHandler;
        var ptr = Marshal.GetFunctionPointerForDelegate(_logCallbackDelegate);
        QuickJSNative.QJS_SetLogCallback(ptr);

        _eventLoop = new EventLoop(this);
        _runtimesByContext[_context] = this;

        // Wire ES module loader bridge (process-global cb routed by ctx → runtime).
        InstallModuleLoader(_runtime);
    }

    /// <summary>
    /// Run a synchronous work function on a background thread, then post
    /// resolve/reject back to the event loop for execution on the JS thread.
    /// </summary>
    internal void Promise(JSValue resolve, JSValue reject, Func<object?> work)
    {
        _eventLoop.TrackAsyncOp();
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                ResolvePromise(resolve, reject, work());
            }
            catch (Exception ex)
            {
                RejectPromise(resolve, reject, ex);
            }
        });
    }

    internal void ResolvePromise(JSValue resolve, JSValue reject, object? result)
    {
        _eventLoop.Post(() =>
        {
            var val = ManagedToJSValue(result);
            var undef = QuickJSNative.QJS_NewUndefined();
            var r = Call(resolve, undef, val);
            FreeValue(r);
            FreeValue(val);
            FreeValue(resolve);
            FreeValue(reject);
        });
    }

    internal void ResolvePromiseWithProjection<T>(JSValue resolve, JSValue reject, T result,
        Func<QuickJSRuntime, T, JSValue> project)
    {
        _eventLoop.Post(() =>
        {
            JSValue val;
            try { val = project(this, result); }
            catch (Exception ex)
            {
                var err = ManagedToJSValue(ex.Message);
                var undef0 = QuickJSNative.QJS_NewUndefined();
                var rr = Call(reject, undef0, err);
                FreeValue(rr); FreeValue(err); FreeValue(resolve); FreeValue(reject);
                return;
            }
            var undef = QuickJSNative.QJS_NewUndefined();
            var r = Call(resolve, undef, val);
            FreeValue(r);
            FreeValue(val);
            FreeValue(resolve);
            FreeValue(reject);
        });
    }

    internal void RejectPromise(JSValue resolve, JSValue reject, Exception ex)
    {
        RejectPromise(resolve, reject, ex.FlatMessage());
    }

    internal void RejectPromise(JSValue resolve, JSValue reject, string message)
    {
        _eventLoop.Post(() =>
        {
            var err = ManagedToJSValue(message);
            var undef = QuickJSNative.QJS_NewUndefined();
            var r = Call(reject, undef, err);
            FreeValue(r);
            FreeValue(err);
            FreeValue(resolve);
            FreeValue(reject);
        });
    }

    private void LogCallbackHandler(int level, string msg)
    {
        OnLog?.Invoke(level, msg);
    }

    /// <summary>
    /// Evaluate JavaScript code and return the result as a managed object.
    /// </summary>
    internal object? Eval(string code, string filename = "<eval>", bool asModule = false)
    {
        int flags = asModule
            ? QuickJSNative.JS_EVAL_TYPE_MODULE | QuickJSNative.JS_EVAL_FLAG_ASYNC
            : QuickJSNative.JS_EVAL_TYPE_GLOBAL;

        var result = Utf8StringHelper.WithUtf8(code, filename, (cp, cl, fp, _) =>
            QuickJSNative.QJS_EvalPtr(_context, cp, cl, fp, flags));

        // Execute any pending jobs (microtasks)
        ExecutePendingJobs();

        if (result.IsException)
        {
            string error = GetExceptionString();
            throw new QuickJSException(error);
        }

        var managed = JSValueToManaged(result);
        QuickJSNative.QJS_FreeValue(_context, result);
        return managed;
    }

    /// <summary>
    /// Evaluate and return raw JSValue (caller is responsible for freeing).
    /// </summary>
    internal JSValue EvalRaw(string code, string filename = "<eval>", int flags = 0)
    {
        return Utf8StringHelper.WithUtf8(code, filename, (cp, cl, fp, _) =>
            QuickJSNative.QJS_EvalPtr(_context, cp, cl, fp, flags));
    }

    /// <summary>
    /// Execute pending microtasks (promise jobs).
    /// </summary>
    internal void ExecutePendingJobs()
    {
        while (QuickJSNative.QJS_IsJobPending(_runtime) != 0)
        {
            int ret = QuickJSNative.QJS_ExecutePendingJob(_runtime, out _);
            if (ret < 0) break;
        }
    }

    /// <summary>
    /// Set a global property to a value.
    /// </summary>
    public void SetGlobal(string name, object value)
    {
        var global = QuickJSNative.QJS_GetGlobalObject(_context);
        var jsVal = ManagedToJSValue(value);
        QuickJSNative.QJS_SetPropertyStr(_context, global, name, jsVal);
        QuickJSNative.QJS_FreeValue(_context, global);
    }

    /// <summary>
    /// Register an opaque-id allocated by a generated binder for cleanup at
    /// runtime disposal.
    /// </summary>
    public void TrackWrappedObjectId(long id) => _wrappedObjectIds.Add(id);

    private readonly object _disposablesLock = new();
    private readonly List<WeakReference<IDisposable>> _trackedDisposables = new();

    /// <summary>
    /// Register an unmanaged-resource holder (e.g. a Stream wrapping a
    /// <see cref="System.IO.FileStream"/> or HTTP response) so that it is
    /// guaranteed to be disposed when the runtime itself is disposed, even
    /// when JavaScript code forgot to call <c>close()</c>. Stored as a
    /// <see cref="WeakReference{T}"/> so it does not prevent GC if the
    /// resource is otherwise unreachable.
    /// </summary>
    public void TrackDisposable(IDisposable resource)
    {
        if (resource is null) return;
        lock (_disposablesLock)
            _trackedDisposables.Add(new WeakReference<IDisposable>(resource));
    }

    /// <summary>
    /// Register a raw native callback as a global JavaScript function. The
    /// callback receives the active context, the JS <c>this</c> value, the
    /// argument count, and a pointer to the argv array, and must return the
    /// resulting <see cref="JSValue"/>. The callback is responsible for its
    /// own argument marshalling and lifetime management.
    /// </summary>
    public void SetGlobalRawFunction(string name,
        Func<IntPtr, JSValue, int, IntPtr, JSValue> handler, int argCount = 0)
    {
        QuickJSNative.JSCFunction nativeFunc = (IntPtr ctx, JSValue thisVal, int argc, IntPtr argv) =>
        {
            try { return handler(ctx, thisVal, argc, argv); }
            catch (Exception ex) { return ThrowInternalError(ctx, ex.Message); }
        };
        var gcHandle = GCHandle.Alloc(nativeFunc);
        _pinnedDelegates.Add(gcHandle);
        var funcPtr = Marshal.GetFunctionPointerForDelegate(nativeFunc);
        var global = QuickJSNative.QJS_GetGlobalObject(_context);
        Utf8StringHelper.WithUtf8(name, (pName, _) =>
        {
            QuickJSNative.QJS_SetPropertyFunctionStrPtr(_context, global, pName, funcPtr, argCount);
            return 0;
        });
        QuickJSNative.QJS_FreeValue(_context, global);
    }

    /// <summary>
    /// Install the static container of a <c>[JSExport]</c>-annotated type as
    /// a global JS object (mirroring its static properties &amp; methods).
    /// AOT-safe: the type parameter is concrete; the binder is registered via
    /// a module initializer.
    /// </summary>
    public void SetGlobalStatic<T>(string name) where T : class
    {
        var binder = QuickJsNet.Interop.JSBinderRegistry.Get<T>()
            ?? throw new InvalidOperationException(
                $"No [JSExport] binder registered for {typeof(T).FullName}. " +
                "Ensure the type is partial and annotated with [JSExport].");
        var container = binder.BuildStaticContainer(this);
        var global = QuickJSNative.QJS_GetGlobalObject(_context);
        QuickJSNative.QJS_SetPropertyStr(_context, global, name, container);
        QuickJSNative.QJS_FreeValue(_context, global);
    }

    /// <summary>
    /// Wrap a managed instance via its registered <see cref="QuickJsNet.Interop.IJSBinder"/>,
    /// returning a JS object. Falls back to a plain primitive conversion when
    /// no binder is registered.
    /// </summary>
    internal JSValue WrapManagedObject(object value)
    {
        if (value is null) return QuickJSNative.QJS_NewNull();
        if (QuickJsNet.Interop.JSBinderRegistry.TryGet(value.GetType(), out var binder)
            && binder is not null)
        {
            // Automatically track [JSExport] resources that own unmanaged
            // state so engine.Dispose() guarantees cleanup even when JS
            // forgot to call close().
            if (value is IDisposable disposable)
                TrackDisposable(disposable);
            return binder.Wrap(this, value);
        }
        return ManagedToJSValue(value);
    }

    /// <summary>
    /// Get a global property value.
    /// </summary>
    public object? GetGlobal(string name)
    {
        var global = QuickJSNative.QJS_GetGlobalObject(_context);
        var val = QuickJSNative.QJS_GetPropertyStr(_context, global, name);
        var result = JSValueToManaged(val);
        QuickJSNative.QJS_FreeValue(_context, val);
        QuickJSNative.QJS_FreeValue(_context, global);
        return result;
    }

    /// <summary>
    /// Register a C# function as a global JavaScript function.
    /// </summary>
    internal void RegisterGlobalFunction(string name, Func<JSValue[], object?> handler, int argCount = 0)
    {
        QuickJSNative.JSCFunction nativeFunc = (IntPtr ctx, JSValue thisVal, int argc, IntPtr argv) =>
        {
            try
            {
                JSValue[] args;
                if (argc == 0)
                {
                    args = Array.Empty<JSValue>();
                }
                else
                {
                    args = new JSValue[argc];
                    unsafe
                    {
                        new ReadOnlySpan<JSValue>((void*)argv, argc).CopyTo(args);
                    }
                }
                var result = handler(args);
                return ManagedToJSValue(result);
            }
            catch (Exception ex)
            {
                return ThrowInternalError(ctx, ex.Message);
            }
        };

        var gcHandle = GCHandle.Alloc(nativeFunc);
        _pinnedDelegates.Add(gcHandle);
        var funcPtr = Marshal.GetFunctionPointerForDelegate(nativeFunc);

        var global = QuickJSNative.QJS_GetGlobalObject(_context);
        Utf8StringHelper.WithUtf8(name, (pName, _) =>
        {
            QuickJSNative.QJS_SetPropertyFunctionStrPtr(_context, global, pName, funcPtr, argCount);
            return 0;
        });
        QuickJSNative.QJS_FreeValue(_context, global);
    }

    /// <summary>
     /// Register a C# function on a specific JS object.
     /// </summary>
    internal void RegisterFunction(JSValue obj, string name, Func<JSValue[], object?> handler, int argCount = 0)
    {
        var funcPtr = MakeFunctionPtr(handler);
        Utf8StringHelper.WithUtf8(name, (pName, _) =>
        {
            QuickJSNative.QJS_SetPropertyFunctionStrPtr(_context, obj, pName, funcPtr, argCount);
            return 0;
        });
    }

    /// <summary>
    /// Build a callable JS function value from a managed delegate, suitable for
    /// use as a module export, object property, or array element.
    /// The delegate is GC-pinned for the lifetime of this runtime.
    /// </summary>
    internal JSValue MakeFunctionValue(string name, Delegate del, int argCount = 0)
    {
        var parameters = del.Method.GetParameters();
        // Detect "spread" delegate signature: (object?[]) -> ?  (params packed into one array)
        bool spreadStyle = parameters.Length == 1
            && parameters[0].ParameterType == typeof(object?[]);

        Func<JSValue[], object?> wrapped = jsArgs =>
        {
            var managedArgs = new object?[jsArgs.Length];
            for (int i = 0; i < jsArgs.Length; i++)
                managedArgs[i] = JSValueToManaged(jsArgs[i]);
            return spreadStyle
                ? del.DynamicInvoke((object?)managedArgs)
                : del.DynamicInvoke(managedArgs);
        };
        var funcPtr = MakeFunctionPtr(wrapped);
        if (argCount <= 0) argCount = spreadStyle ? 0 : parameters.Length;
        return QuickJSNative.QJS_NewCFunction(_context, funcPtr, name, argCount);
    }

    private IntPtr MakeFunctionPtr(Func<JSValue[], object?> handler)
    {
        QuickJSNative.JSCFunction nativeFunc = (IntPtr ctx, JSValue thisVal, int argc, IntPtr argv) =>
        {
            try
            {
                JSValue[] args;
                if (argc == 0)
                {
                    args = Array.Empty<JSValue>();
                }
                else
                {
                    args = new JSValue[argc];
                    unsafe
                    {
                        new ReadOnlySpan<JSValue>((void*)argv, argc).CopyTo(args);
                    }
                }
                var result = handler(args);
                return ManagedToJSValue(result);
            }
            catch (Exception ex)
            {
                return ThrowInternalError(ctx, ex.Message);
            }
        };
        var gcHandle = GCHandle.Alloc(nativeFunc);
        _pinnedDelegates.Add(gcHandle);
        return Marshal.GetFunctionPointerForDelegate(nativeFunc);
    }


    /// <summary>
    /// Call a JavaScript function. Uses stack-allocated argv for ≤16 arguments.
    /// </summary>
    internal JSValue Call(JSValue func, JSValue thisObj, params JSValue[] args)
    {
        return CallFast(func, thisObj, args.AsSpan());
    }

    /// <summary>
    /// Trigger garbage collection.
    /// </summary>
    public void RunGC()
    {
        QuickJSNative.QJS_RunGC(_runtime);
    }

    /// <summary>
    /// Get the QuickJS version string.
    /// </summary>
    public static string GetVersion()
    {
        IntPtr ptr = QuickJSNative.QJS_GetVersion();
        return PtrToStringUTF8(ptr) ?? "";
    }

    #region Helper Methods

    /// <summary>
    /// Convert a managed value to JSValue.
    /// </summary>
    internal JSValue ManagedToJSValue(object? value)
    {
        switch (value)
        {
            case null:
                return QuickJSNative.QJS_NewNull();
            case bool b:
                return QuickJSNative.QJS_NewBool(_context, b ? 1 : 0);
            case int i:
                return QuickJSNative.QJS_NewInt32(_context, i);
            case long l:
                return QuickJSNative.QJS_NewInt64(_context, l);
            case float f:
                return QuickJSNative.QJS_NewFloat64(_context, f);
            case double d:
                return QuickJSNative.QJS_NewFloat64(_context, d);
            case string s:
                return QuickJSNative.QJS_NewString(_context, s);
            case byte[] bytes:
                return NewArrayBuffer(bytes);
            case JSValue jsv:
                return QuickJSNative.QJS_DupValue(_context, jsv);
            default:
                // Look up [JSExport]-generated binder if present.
                if (QuickJsNet.Interop.JSBinderRegistry.TryGet(value.GetType(), out var binder)
                    && binder is not null)
                    return binder.Wrap(this, value);
                return QuickJSNative.QJS_NewString(_context, value.ToString() ?? "");
        }
    }

    /// <summary>
    /// 从非托管 UTF-8 字符串指针转换为托管 string。
    /// 替代 .NET Core 的 Marshal.PtrToStringUTF8。
    /// </summary> 
    private static unsafe string? PtrToStringUTF8(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero)
            return null;

        // 计算以 null 结尾的字节长度
        byte* p = (byte*)ptr;
        int len = 0;
        while (p[len] != 0)
            len++;

        if (len == 0)
            return string.Empty;

        return Encoding.UTF8.GetString(p, len);
    }

    private static unsafe string? PtrToStringUTF8(IntPtr ptr, uint len)
    {
        if (ptr == IntPtr.Zero) return null;
        if (len == 0) return string.Empty;
        if (len > int.MaxValue) len = int.MaxValue;

        return Encoding.UTF8.GetString((byte*)ptr, (int)len);
    }

    /// <summary>
    /// Convert a JSValue to a managed object.
    /// </summary>
    internal object? JSValueToManaged(JSValue val)
    {
        if (val.IsNull || val.IsUndefined)
            return null;
        if (val.IsBool)
            return QuickJSNative.QJS_ToBool(_context, val) != 0;
        if (val.IsNumber)
        {
            if (val.Tag == JSValue.TAG_INT)
                return val.Int32;
            QuickJSNative.QJS_ToFloat64(_context, out double d, val);
            return d;
        }
        if (val.IsString)
        {
            return GetString(val);
        }
        if (val.IsException)
            return null;
        // For objects/arrays, return as JSON string
        if (val.IsObject)
        {
            var json = QuickJSNative.QJS_JSONStringify(_context, val);
            if (!json.IsException && json.IsString)
            {
                // IntPtr cstr = QuickJSNative.QJS_ToCStringLen(_context, out var len, json);
                string? result = GetString(json);
                QuickJSNative.QJS_FreeValue(_context, json);
                return result;
                //if (cstr != IntPtr.Zero)
                //{
                //    string result = PtrToStringUTF8(cstr, len.ToUInt32());
                //    QuickJSNative.QJS_FreeCString(_context, cstr);
                //    return result;
                //}
            }
            else
            {
                QuickJSNative.QJS_FreeValue(_context, json);
            }
            return "[object]";
        }
        return null;
    }

    /// <summary>
    /// Extract a string from a JSValue without freeing it.
    /// </summary>
    /// <remarks>
    /// The result may be null;
    /// </remarks>
    internal string? GetString(JSValue val)
    {
        IntPtr cstr = QuickJSNative.QJS_ToCStringLen(_context, out var len, val);
        if (cstr == IntPtr.Zero) return null;
        string? result = PtrToStringUTF8(cstr, len.ToUInt32());
        QuickJSNative.QJS_FreeCString(_context, cstr);
        return result;
    }

    /// <summary>
    /// Converts the specified JavaScript value to a UTF-8 encoded byte array.
    /// </summary>
    /// <param name="val">The JavaScript value to convert to a UTF-8 encoded byte array.</param>
    /// <returns>A byte array containing the UTF-8 encoded representation of the value. Returns an empty array if the
    /// conversion fails.</returns>
    internal byte[] GetStringBytesUTF8(JSValue val)
    {
        IntPtr cstr = QuickJSNative.QJS_ToCStringLen(_context, out var len, val);
        if (cstr == IntPtr.Zero) return Array.Empty<byte>();
        byte[] data = new byte[len.ToUInt32()];
        Marshal.Copy(cstr, data, 0, (int)len);
        QuickJSNative.QJS_FreeCString(_context, cstr);
        return data;
    }

    /// <summary>
    /// Extract a byte array from an ArrayBuffer JSValue.
    /// </summary>
    /// <param name="val"></param>
    /// <returns>
    /// The byte array extracted from the ArrayBuffer, or null if the value is not an ArrayBuffer or if extraction fails.
    /// </returns>
    internal byte[]? GetByteArray(JSValue val)
    {
        var bufPtr = QuickJSNative.QJS_GetArrayBuffer(_context, out var sizePtr, val);
        if (bufPtr == IntPtr.Zero) return null;
        var size = sizePtr.ToUInt64();
        if (size == 0) return Array.Empty<byte>();
        byte[] data = new byte[size];
        Marshal.Copy(bufPtr, data, 0, (int)size);
        return data;
    }

    /// <summary>
    /// Get the integer value from a JSValue.
    /// </summary>
    internal int GetInt32(JSValue val)
    {
        QuickJSNative.QJS_ToInt32(_context, out int result, val);
        return result;
    }

    /// <summary>
    /// Get the double value from a JSValue.
    /// </summary>
    internal double GetFloat64(JSValue val)
    {
        QuickJSNative.QJS_ToFloat64(_context, out double result, val);
        return result;
    }

    /// <summary>
    /// Create an ArrayBuffer from a byte array.
    /// </summary>
    internal JSValue NewArrayBuffer(byte[] data)
    {
        unsafe
        {
            fixed (byte* ptr = data)
            {
                return QuickJSNative.QJS_NewArrayBufferCopy(_context, (IntPtr)ptr, (UIntPtr)data.Length);
            }
        }
    }

    /// <summary>
    /// Free a JSValue.
    /// </summary>
    internal void FreeValue(JSValue val)
    {
        QuickJSNative.QJS_FreeValue(_context, val);
    }

    /// <summary>
    /// Duplicate a JSValue (increment reference count).
    /// </summary>
    internal JSValue DupValue(JSValue val)
    {
        return QuickJSNative.QJS_DupValue(_context, val);
    }

    private string GetExceptionString()
    {
        var ex = QuickJSNative.QJS_GetException(_context);
        // IntPtr cstr = QuickJSNative.QJS_ToCStringLen(_context, out var len, ex);
        string msg = GetString(ex) ?? "Unknown error";
        //if (cstr != IntPtr.Zero)
        //{
        //    msg = PtrToStringUTF8(cstr, len.ToUInt32()) ?? "Unknown error";
        //    QuickJSNative.QJS_FreeCString(_context, cstr);
        //}

        // Try to get stack trace
        if (QuickJSNative.QJS_IsError(_context, ex) != 0)
        {
            var stack = QuickJSNative.QJS_GetPropertyStr(_context, ex, "stack");
            if (stack.IsString)
            {
                //IntPtr stackStr = QuickJSNative.QJS_ToCStringLen(_context, out var len2, stack);
                //if (stackStr != IntPtr.Zero)
                //{
                //    msg += "\n" + PtrToStringUTF8(stackStr, len2.ToUInt32());
                //    QuickJSNative.QJS_FreeCString(_context, stackStr);
                //}
                msg += "\n" + (GetString(stack) ?? "");
            }
            QuickJSNative.QJS_FreeValue(_context, stack);
        }

        QuickJSNative.QJS_FreeValue(_context, ex);
        return msg;
    }

    /// <summary>
    /// 获取全局作用域中指定名称的函数（如果存在），并返回其 JSValue 表示；通过 Call 方法调用该函数时，必须确保在调用完成后释放返回的 JSValue 以避免内存泄漏。
    /// </summary>
    /// <param name="name">函数名称</param>
    /// <param name="func">如果函数存在，返回其 JSValue 表示</param>
    /// <returns>如果函数存在，返回 true；否则返回 false</returns>
    internal bool TryGetFunction(string name, out JSValue func)
    {
        var global = QuickJSNative.QJS_GetGlobalObject(_context);
        var val = QuickJSNative.QJS_GetPropertyStr(_context, global, name);
        if (QuickJSNative.QJS_IsFunction(_context, val) != 0)
        {
            func = val;
            QuickJSNative.QJS_FreeValue(_context, global);
            return true;
        }
        QuickJSNative.QJS_FreeValue(_context, val);
        QuickJSNative.QJS_FreeValue(_context, global);
        func = default;
        return false;
    }

    #endregion

    #region IJsEngine Implementation

    /// <summary>
    /// 设置全局变量（IJsEngine 接口适配）
    /// </summary>
    public void SetGlobalVariable(string name, object value)
    {
        SetGlobal(name, value);
    }

    /// <summary>
    /// 执行 JS 脚本（IJsEngine 接口适配）
    /// </summary>
    public object? Execute(string scriptCode, string fileName = "")
    {
        return _eventLoop.Execute(scriptCode, fileName);
    }

    /// <summary>
    /// Execute JavaScript code as an ES module (supports <c>import</c>/<c>export</c>
    /// and top-level <c>await</c>). The event loop is pumped until the module
    /// has finished evaluating.
    /// </summary>
    public object? ExecuteModule(string scriptCode, string fileName = "<module>")
    {
        return _eventLoop.Execute(scriptCode, fileName, asModule: true);
    }

    /// <summary>
    /// 检查全局作用域中是否存在指定名称的函数
    /// </summary>
    public bool HasFunction(string functionName)
    {
        var global = QuickJSNative.QJS_GetGlobalObject(_context);
        var val = QuickJSNative.QJS_GetPropertyStr(_context, global, functionName);
        bool isFunc = QuickJSNative.QJS_IsFunction(_context, val) != 0;
        QuickJSNative.QJS_FreeValue(_context, val);
        QuickJSNative.QJS_FreeValue(_context, global);
        return isFunc;
    }

    /// <summary>
    /// 调用全局作用域中的 JS 函数并返回托管对象
    /// </summary>kyh
    public object? InvokeFunction(string functionName, params object[] args)
    {
        return _eventLoop.Invoke(functionName, args);
    }

    #endregion

    #region Dispose Pattern

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;
        _disposed = true;

        if (disposing)
        {
            foreach (var handle in _pinnedDelegates)
            {
                if (handle.IsAllocated)
                    handle.Free();
            }
            _pinnedDelegates.Clear();
        }

        if (_context != IntPtr.Zero)
        {
            _runtimesByContext.TryRemove(_context, out _);

            // Force-close any [JSExport] resources (Stream, DirectoryStream,
            // FetchResponse...) that JS code never explicitly closed. Held as
            // weak refs so we ignore ones already collected.
            List<IDisposable>? live = null;
            lock (_disposablesLock)
            {
                foreach (var wr in _trackedDisposables)
                {
                    if (wr.TryGetTarget(out var d))
                    {
                        (live ??= new List<IDisposable>()).Add(d);
                    }
                }
                _trackedDisposables.Clear();
            }
            if (live is not null)
            {
                foreach (var d in live)
                {
                    try { d.Dispose(); } catch { /* swallow: best-effort cleanup */ }
                }
            }

            // Release any object-table ids we created for [JSExport] wrappers
            // associated with this runtime.
            foreach (var id in _wrappedObjectIds)
                QuickJsNet.Interop.JSObjectTable.Release(id);
            _wrappedObjectIds.Clear();

            QuickJSNative.QJS_FreeContext(_context);
            _context = IntPtr.Zero;
        }

        if (_runtime != IntPtr.Zero)
        {
            QuickJSNative.QJS_StdFreeHandlers(_runtime);
            QuickJSNative.QJS_FreeRuntime(_runtime);
            _runtime = IntPtr.Zero;
        }
    }

    ~QuickJSRuntime()
    {
        Dispose(false);
    }

    #endregion 
}
