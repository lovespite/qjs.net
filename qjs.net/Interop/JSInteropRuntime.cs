using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using QuickJSNet.Bindings;

namespace QuickJsNet.Interop;

/// <summary>
/// Static helpers for use inside source-generated, AOT-safe JS callbacks.
/// All helpers are pure functions over <c>(IntPtr ctx, …)</c> – they never
/// allocate <see cref="System.Runtime.InteropServices.GCHandle"/> on the hot
/// path, never use reflection, and never capture managed delegates.
/// <para>
/// The hidden id property used to identify the wrapped C# instance on a JS
/// wrapper object: <c>__qjs_id__</c>.
/// </para>
/// </summary>
public static class JSInteropRuntime
{
    /// <summary>The hidden property name carrying the managed-object id.</summary>
    public const string IdPropertyName = "__qjs_id__";

    // ───────────────────── thisVal → managed instance ─────────────────────

    /// <summary>Read the hidden id from a JS <c>this</c> object. Returns 0 if missing.</summary>
    public static long ReadId(IntPtr ctx, JSValue thisVal)
    {
        var idVal = QuickJSNative.QJS_GetPropertyStr(ctx, thisVal, IdPropertyName);
        try
        {
            if (idVal.Tag == JSValue.TAG_INT)
                return idVal.Int32;
            if (QuickJSNative.QJS_ToInt64(ctx, out long l, idVal) == 0)
                return l;
            return 0;
        }
        finally
        {
            QuickJSNative.QJS_FreeValue(ctx, idVal);
        }
    }

    /// <summary>Resolve <c>this</c> back to the strongly-typed managed instance.</summary>
    public static T? Unwrap<T>(IntPtr ctx, JSValue thisVal) where T : class
        => JSObjectTable.Get<T>(ReadId(ctx, thisVal));

    // ───────────────────── Argument readers ─────────────────────

    /// <summary>Read the i-th JS argument as a raw <see cref="JSValue"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe JSValue ArgAt(IntPtr argv, int i)
        => ((JSValue*)argv)[i];

    public static int ArgInt32(IntPtr ctx, IntPtr argv, int argc, int i, int defaultValue = 0)
    {
        if (i >= argc) return defaultValue;
        var v = ArgAt(argv, i);
        if (v.Tag == JSValue.TAG_INT) return v.Int32;
        if (QuickJSNative.QJS_ToInt32(ctx, out int r, v) == 0) return r;
        return defaultValue;
    }

    public static long ArgInt64(IntPtr ctx, IntPtr argv, int argc, int i, long defaultValue = 0)
    {
        if (i >= argc) return defaultValue;
        var v = ArgAt(argv, i);
        if (v.Tag == JSValue.TAG_INT) return v.Int32;
        if (QuickJSNative.QJS_ToInt64(ctx, out long r, v) == 0) return r;
        return defaultValue;
    }

    public static double ArgFloat64(IntPtr ctx, IntPtr argv, int argc, int i, double defaultValue = 0.0)
    {
        if (i >= argc) return defaultValue;
        var v = ArgAt(argv, i);
        if (v.Tag == JSValue.TAG_INT) return v.Int32;
        if (QuickJSNative.QJS_ToFloat64(ctx, out double r, v) == 0) return r;
        return defaultValue;
    }

    public static float ArgFloat32(IntPtr ctx, IntPtr argv, int argc, int i, float defaultValue = 0f)
        => (float)ArgFloat64(ctx, argv, argc, i, defaultValue);

    public static bool ArgBool(IntPtr ctx, IntPtr argv, int argc, int i, bool defaultValue = false)
    {
        if (i >= argc) return defaultValue;
        var v = ArgAt(argv, i);
        if (v.IsBool) return v.Int32 != 0;
        return QuickJSNative.QJS_ToBool(ctx, v) != 0;
    }

    public static string? ArgString(IntPtr ctx, IntPtr argv, int argc, int i)
    {
        if (i >= argc) return null;
        var v = ArgAt(argv, i);
        if (v.IsNullOrUndefined) return null;
        return ReadString(ctx, v);
    }

    public static T? ArgObject<T>(IntPtr ctx, IntPtr argv, int argc, int i) where T : class
    {
        if (i >= argc) return null;
        var v = ArgAt(argv, i);
        if (!v.IsObject) return null;
        return JSObjectTable.Get<T>(ReadId(ctx, v));
    }

    public static byte[]? ArgBytes(IntPtr ctx, IntPtr argv, int argc, int i)
    {
        if (i >= argc) return null;
        var v = ArgAt(argv, i);
        if (v.IsNullOrUndefined) return null;
        var bufPtr = QuickJSNative.QJS_GetArrayBuffer(ctx, out var sizePtr, v);
        if (bufPtr == IntPtr.Zero)
        {
            // Try string fallback
            var s = ReadString(ctx, v);
            return s is null ? null : System.Text.Encoding.UTF8.GetBytes(s);
        }
        var size = (int)sizePtr.ToUInt32();
        var arr = new byte[size];
        if (size > 0) Marshal.Copy(bufPtr, arr, 0, size);
        return arr;
    }

    // ───────────────────── String helpers ─────────────────────

    internal static string? ReadString(IntPtr ctx, JSValue v)
    {
        var ptr = QuickJSNative.QJS_ToCStringLen(ctx, out var len, v);
        if (ptr == IntPtr.Zero) return null;
        try
        {
            unsafe
            {
                var n = (int)len.ToUInt32();
                return n == 0 ? string.Empty : System.Text.Encoding.UTF8.GetString((byte*)ptr, n);
            }
        }
        finally { QuickJSNative.QJS_FreeCString(ctx, ptr); }
    }

    // ───────────────────── Result writers ─────────────────────

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JSValue Undefined() => QuickJSNative.QJS_NewUndefined();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JSValue Null() => QuickJSNative.QJS_NewNull();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JSValue Bool(IntPtr ctx, bool v) => QuickJSNative.QJS_NewBool(ctx, v ? 1 : 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JSValue Int32(IntPtr ctx, int v) => QuickJSNative.QJS_NewInt32(ctx, v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JSValue Int64(IntPtr ctx, long v) => QuickJSNative.QJS_NewInt64(ctx, v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JSValue Float64(IntPtr ctx, double v) => QuickJSNative.QJS_NewFloat64(ctx, v);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static JSValue String(IntPtr ctx, string? v)
        => v is null ? QuickJSNative.QJS_NewNull() : QuickJSNative.QJS_NewString(ctx, v);

    public static unsafe JSValue Bytes(IntPtr ctx, byte[]? data)
    {
        if (data is null) return QuickJSNative.QJS_NewNull();
        if (data.Length == 0)
            return QuickJSNative.QJS_NewArrayBufferCopy(ctx, IntPtr.Zero, 0);
        fixed (byte* p = data)
            return QuickJSNative.QJS_NewArrayBufferCopy(ctx, (IntPtr)p, (nuint)data.Length);
    }

    /// <summary>
    /// Wrap a managed object that has a registered binder; otherwise returns
    /// a primitive conversion or null.
    /// </summary>
    public static JSValue ManagedObject(IntPtr ctx, object? value)
    {
        if (value is null) return QuickJSNative.QJS_NewNull();
        var t = value.GetType();
        if (JSBinderRegistry.TryGet(t, out var binder) && binder is not null)
            return WrapWithBinder(ctx, value, binder);
        // Primitive fallthroughs
        return value switch
        {
            bool b => QuickJSNative.QJS_NewBool(ctx, b ? 1 : 0),
            int i => QuickJSNative.QJS_NewInt32(ctx, i),
            long l => QuickJSNative.QJS_NewInt64(ctx, l),
            float f => QuickJSNative.QJS_NewFloat64(ctx, f),
            double d => QuickJSNative.QJS_NewFloat64(ctx, d),
            string s => QuickJSNative.QJS_NewString(ctx, s),
            byte[] ba => Bytes(ctx, ba),
            _ => QuickJSNative.QJS_NewString(ctx, value.ToString() ?? ""),
        };
    }

    private static JSValue WrapWithBinder(IntPtr ctx, object value, IJSBinder binder)
    {
        var rt = QuickJsNet.Core.QuickJSRuntime.FromContext(ctx);
        return rt is null ? QuickJSNative.QJS_NewNull() : binder.Wrap(rt, value);
    }

    // ───────────────────── Errors ─────────────────────

    public static JSValue ThrowTypeError(IntPtr ctx, string message)
        => QuickJSNative.QJS_ThrowTypeError(ctx, message);

    public static JSValue ThrowInternal(IntPtr ctx, Exception ex)
        => QuickJSNative.QJS_ThrowInternalError(ctx, ex.Message);

    public static JSValue ThrowMissingTarget(IntPtr ctx)
        => QuickJSNative.QJS_ThrowTypeError(ctx, "QuickJsNet: detached or invalid managed instance");

    // ───────────────────── Object construction (proto-based) ─────────────────────

    /// <summary>
    /// Build a fresh JS object whose hidden id property points to the registered
    /// managed instance. The object is freshly allocated; properties/methods are
    /// installed on a per-class prototype via <see cref="InstallProtoFunction"/>
    /// and <see cref="InstallProtoAccessor"/>.
    /// </summary>
    public static JSValue NewWrapper(IntPtr ctx, JSValue prototype, long id)
        => NewWrapper(ctx, prototype, id, useIndexerProxy: false);

    /// <summary>
    /// Same as <see cref="NewWrapper(IntPtr, JSValue, long)"/> but, when
    /// <paramref name="useIndexerProxy"/> is true, wraps the result in a
    /// <c>Proxy</c> that forwards numeric property access to <c>__idxGet</c> /
    /// <c>__idxSet</c> methods on the underlying object.
    /// </summary>
    public static JSValue NewWrapper(IntPtr ctx, JSValue prototype, long id, bool useIndexerProxy)
    {
        var obj = QuickJSNative.QJS_NewObject(ctx);
        if (prototype.IsObject)
        {
            var global = QuickJSNative.QJS_GetGlobalObject(ctx);
            var objectCtor = QuickJSNative.QJS_GetPropertyStr(ctx, global, "Object");
            var setProto = QuickJSNative.QJS_GetPropertyStr(ctx, objectCtor, "setPrototypeOf");
            unsafe
            {
                JSValue* args = stackalloc JSValue[2];
                args[0] = obj;
                args[1] = prototype;
                var res = QuickJSNative.QJS_Call(ctx, setProto, QuickJSNative.QJS_NewUndefined(),
                    2, (IntPtr)args);
                QuickJSNative.QJS_FreeValue(ctx, res);
            }
            QuickJSNative.QJS_FreeValue(ctx, setProto);
            QuickJSNative.QJS_FreeValue(ctx, objectCtor);
            QuickJSNative.QJS_FreeValue(ctx, global);
        }
        var idVal = QuickJSNative.QJS_NewInt64(ctx, id);
        QuickJSNative.QJS_DefinePropertyValueStr(ctx, obj, IdPropertyName, idVal, 0);

        if (!useIndexerProxy) return obj;

        // Wrap in a Proxy whose get/set traps forward integer-keyed access to
        // __idxGet / __idxSet. Done by JS-evaluating a tiny factory once per
        // context and caching it on the global as __qjs_makeIdxProxy.
        var ctxGlobal = QuickJSNative.QJS_GetGlobalObject(ctx);
        var factory = QuickJSNative.QJS_GetPropertyStr(ctx, ctxGlobal, "__qjs_makeIdxProxy");
        if (!factory.IsObject)
        {
            QuickJSNative.QJS_FreeValue(ctx, factory);
            const string src = "globalThis.__qjs_makeIdxProxy = function(o){return new Proxy(o,{get(t,k,r){if(typeof k==='string'&&k!==''&&!isNaN(+k))return t.__idxGet(+k);return Reflect.get(t,k,r);},set(t,k,v,r){if(typeof k==='string'&&k!==''&&!isNaN(+k)){if(typeof t.__idxSet==='function'){t.__idxSet(+k,v);return true;}return false;}return Reflect.set(t,k,v,r);}});}";
            var ev = QuickJSNative.QJS_Eval(ctx, src, (nuint)System.Text.Encoding.UTF8.GetByteCount(src), "<qjs-idx-proxy>", 0);
            QuickJSNative.QJS_FreeValue(ctx, ev);
            factory = QuickJSNative.QJS_GetPropertyStr(ctx, ctxGlobal, "__qjs_makeIdxProxy");
        }
        JSValue wrapped;
        unsafe
        {
            JSValue* args = stackalloc JSValue[1];
            args[0] = obj;
            wrapped = QuickJSNative.QJS_Call(ctx, factory, QuickJSNative.QJS_NewUndefined(), 1, (IntPtr)args);
        }
        QuickJSNative.QJS_FreeValue(ctx, factory);
        QuickJSNative.QJS_FreeValue(ctx, ctxGlobal);
        QuickJSNative.QJS_FreeValue(ctx, obj);
        return wrapped;
    }

    /// <summary>
    /// Install a static unmanaged function as a method on the given object
    /// (typically a class prototype).
    /// </summary>
    public static void InstallProtoFunction(IntPtr ctx, JSValue proto, string name, IntPtr funcPtr, int argCount)
    {
        QuickJSNative.QJS_SetPropertyFunctionStr(ctx, proto, name, funcPtr, argCount);
    }

    /// <summary>
    /// Install a getter/setter accessor pair on the given object using
    /// <c>Object.defineProperty</c>. Either getter or setter may be
    /// <see cref="IntPtr.Zero"/>.
    /// </summary>
    public static unsafe void InstallProtoAccessor(IntPtr ctx, JSValue proto, string name,
        IntPtr getterPtr, IntPtr setterPtr)
    {
        // Build a JS descriptor object: { get, set, configurable: true, enumerable: true }
        var desc = QuickJSNative.QJS_NewObject(ctx);
        if (getterPtr != IntPtr.Zero)
        {
            var g = QuickJSNative.QJS_NewCFunction(ctx, getterPtr, name, 0);
            QuickJSNative.QJS_SetPropertyStr(ctx, desc, "get", g);
        }
        if (setterPtr != IntPtr.Zero)
        {
            var s = QuickJSNative.QJS_NewCFunction(ctx, setterPtr, name, 1);
            QuickJSNative.QJS_SetPropertyStr(ctx, desc, "set", s);
        }
        QuickJSNative.QJS_SetPropertyStr(ctx, desc, "configurable",
            QuickJSNative.QJS_NewBool(ctx, 1));
        QuickJSNative.QJS_SetPropertyStr(ctx, desc, "enumerable",
            QuickJSNative.QJS_NewBool(ctx, 1));

        var global = QuickJSNative.QJS_GetGlobalObject(ctx);
        var objectCtor = QuickJSNative.QJS_GetPropertyStr(ctx, global, "Object");
        var defineProp = QuickJSNative.QJS_GetPropertyStr(ctx, objectCtor, "defineProperty");

        var nameVal = QuickJSNative.QJS_NewString(ctx, name);
        JSValue* args = stackalloc JSValue[3];
        args[0] = proto;
        args[1] = nameVal;
        args[2] = desc;
        var res = QuickJSNative.QJS_Call(ctx, defineProp, QuickJSNative.QJS_NewUndefined(),
            3, (IntPtr)args);
        QuickJSNative.QJS_FreeValue(ctx, res);
        QuickJSNative.QJS_FreeValue(ctx, nameVal);
        QuickJSNative.QJS_FreeValue(ctx, desc);
        QuickJSNative.QJS_FreeValue(ctx, defineProp);
        QuickJSNative.QJS_FreeValue(ctx, objectCtor);
        QuickJSNative.QJS_FreeValue(ctx, global);
    }
}
