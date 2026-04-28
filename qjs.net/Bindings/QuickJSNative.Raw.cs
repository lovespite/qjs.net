using System.Runtime.InteropServices;

namespace QuickJSNet.Bindings;

/// <summary>
/// Raw / zero-marshalling P/Invoke declarations.
/// <para>
/// These overloads bind to the same native exports as their managed-string
/// counterparts in <see cref="QuickJSNative"/>, but accept <see cref="IntPtr"/>
/// for UTF-8 string buffers — bypassing the runtime marshaller's allocation
/// and UTF-16 → UTF-8 conversion on every call.
/// </para>
/// <para>
/// Callers are responsible for providing valid, NUL-terminated UTF-8 buffers
/// where required (the C functions follow standard libc conventions). Use
/// <c>QuickJsNet.Utils.Utf8StringHelper.WithUtf8</c> to encode a managed
/// <c>string</c> into a stack / pooled buffer with a single allocation.
/// </para>
/// </summary>
public static partial class QuickJSNative
{
    // Native ArrayBuffer free callback. Signature:
    //   void(JSRuntime *rt, void *opaque, void *ptr)
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void FreeArrayBufferDataFunc(IntPtr rt, IntPtr opaque, IntPtr ptr);

    // ============= Zero-copy ArrayBuffer (NEW exports) =============

    /// <summary>
    /// Wrap an externally-owned buffer as a JS ArrayBuffer without copying.
    /// <paramref name="freeFunc"/> (may be <see cref="IntPtr.Zero"/>) is invoked
    /// when the ArrayBuffer is GC-collected.
    /// </summary>
    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_NewArrayBuffer(
        IntPtr ctx, IntPtr buf, nuint len,
        IntPtr freeFunc, IntPtr opaque, int isShared);

    /// <summary>Detach an ArrayBuffer (length becomes 0).</summary>
    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_DetachArrayBuffer(IntPtr ctx, JSValue obj);

    // ============= Pointer-based string overloads (no marshaller alloc) =============

    [LibraryImport(DLL_NAME, EntryPoint = "QJS_Eval")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_EvalPtr(IntPtr ctx, IntPtr inputUtf8, nuint inputLen,
        IntPtr filenameUtf8, int evalFlags);

    [LibraryImport(DLL_NAME, EntryPoint = "QJS_DetectModule")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_DetectModulePtr(IntPtr inputUtf8, nuint inputLen);

    [LibraryImport(DLL_NAME, EntryPoint = "QJS_NewString")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_NewStringPtr(IntPtr ctx, IntPtr utf8);

    [LibraryImport(DLL_NAME, EntryPoint = "QJS_GetPropertyStr")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_GetPropertyStrPtr(IntPtr ctx, JSValue thisObj, IntPtr propUtf8);

    [LibraryImport(DLL_NAME, EntryPoint = "QJS_SetPropertyStr")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_SetPropertyStrPtr(IntPtr ctx, JSValue thisObj,
        IntPtr propUtf8, JSValue val);

    [LibraryImport(DLL_NAME, EntryPoint = "QJS_DeletePropertyStr")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_DeletePropertyStrPtr(IntPtr ctx, JSValue thisObj, IntPtr propUtf8);

    [LibraryImport(DLL_NAME, EntryPoint = "QJS_DefinePropertyValueStr")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_DefinePropertyValueStrPtr(IntPtr ctx, JSValue thisObj,
        IntPtr propUtf8, JSValue val, int flags);

    [LibraryImport(DLL_NAME, EntryPoint = "QJS_NewCFunction")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_NewCFunctionPtr(IntPtr ctx, IntPtr func,
        IntPtr nameUtf8, int length);

    [LibraryImport(DLL_NAME, EntryPoint = "QJS_SetPropertyFunctionStr")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_SetPropertyFunctionStrPtr(IntPtr ctx, JSValue obj,
        IntPtr nameUtf8, IntPtr func, int length);

    [LibraryImport(DLL_NAME, EntryPoint = "QJS_ThrowTypeError")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_ThrowTypeErrorPtr(IntPtr ctx, IntPtr msgUtf8);

    [LibraryImport(DLL_NAME, EntryPoint = "QJS_ThrowReferenceError")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_ThrowReferenceErrorPtr(IntPtr ctx, IntPtr msgUtf8);

    [LibraryImport(DLL_NAME, EntryPoint = "QJS_ThrowInternalError")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_ThrowInternalErrorPtr(IntPtr ctx, IntPtr msgUtf8);

    [LibraryImport(DLL_NAME, EntryPoint = "QJS_ParseJSON")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_ParseJSONPtr(IntPtr ctx, IntPtr bufUtf8, nuint bufLen,
        IntPtr filenameUtf8);
}
