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
public class QuickJSRuntime
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
            ? QuickJSNative.JS_EVAL_TYPE_MODULE
            : QuickJSNative.JS_EVAL_TYPE_GLOBAL;

        var result = QuickJSNative.QJS_Eval(_context, code, (UIntPtr)Encoding.UTF8.GetByteCount(code),
            filename, flags);

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
        return QuickJSNative.QJS_Eval(_context, code,
            (UIntPtr)Encoding.UTF8.GetByteCount(code), filename, flags);
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
                var args = new JSValue[argc];
                for (int i = 0; i < argc; i++)
                {
                    args[i] = Marshal.PtrToStructure<JSValue>(argv + i * Marshal.SizeOf<JSValue>());
                }
                var result = handler(args);
                return ManagedToJSValue(result);
            }
            catch (Exception ex)
            {
                return QuickJSNative.QJS_ThrowInternalError(ctx, ex.Message);
            }
        };

        var gcHandle = GCHandle.Alloc(nativeFunc);
        _pinnedDelegates.Add(gcHandle);
        var funcPtr = Marshal.GetFunctionPointerForDelegate(nativeFunc);

        var global = QuickJSNative.QJS_GetGlobalObject(_context);
        QuickJSNative.QJS_SetPropertyFunctionStr(_context, global, name, funcPtr, argCount);
        QuickJSNative.QJS_FreeValue(_context, global);
    }

    /// <summary>
    /// Register a C# function on a specific JS object.
    /// </summary>
    internal void RegisterFunction(JSValue obj, string name, Func<JSValue[], object?> handler, int argCount = 0)
    {
        QuickJSNative.JSCFunction nativeFunc = (IntPtr ctx, JSValue thisVal, int argc, IntPtr argv) =>
        {
            try
            {
                var args = new JSValue[argc];
                for (int i = 0; i < argc; i++)
                {
                    args[i] = Marshal.PtrToStructure<JSValue>(argv + i * Marshal.SizeOf<JSValue>());
                }
                var result = handler(args);
                return ManagedToJSValue(result);
            }
            catch (Exception ex)
            {
                return QuickJSNative.QJS_ThrowInternalError(ctx, ex.Message);
            }
        };

        var gcHandle = GCHandle.Alloc(nativeFunc);
        _pinnedDelegates.Add(gcHandle);
        var funcPtr = Marshal.GetFunctionPointerForDelegate(nativeFunc);

        QuickJSNative.QJS_SetPropertyFunctionStr(_context, obj, name, funcPtr, argCount);
    }


    /// <summary>
    /// Call a JavaScript function.
    /// </summary>
    internal JSValue Call(JSValue func, JSValue thisObj, params JSValue[] args)
    {
        if (args.Length == 0)
        {
            return QuickJSNative.QJS_Call(_context, func, thisObj, 0, IntPtr.Zero);
        }

        int size = Marshal.SizeOf<JSValue>();
        IntPtr argsPtr = Marshal.AllocHGlobal(size * args.Length);
        try
        {
            for (int i = 0; i < args.Length; i++)
                Marshal.StructureToPtr(args[i], argsPtr + i * size, false);
            return QuickJSNative.QJS_Call(_context, func, thisObj, args.Length, argsPtr);
        }
        finally
        {
            Marshal.FreeHGlobal(argsPtr);
        }
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
