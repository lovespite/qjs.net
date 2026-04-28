using System.Runtime.InteropServices;

namespace QuickJSNet.Bindings;

/// <summary>
/// JSValue is a 16-byte struct on x64 (non NaN-boxing mode).
/// Layout: union(8 bytes) + int64 tag(8 bytes)
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct JSValue
{
    [FieldOffset(0)] public int Int32;
    [FieldOffset(0)] public double Float64;
    [FieldOffset(0)] public IntPtr Ptr;
    [FieldOffset(0)] public long ShortBigInt;
    [FieldOffset(8)] public long Tag;

    // Tag constants
    public const int TAG_BIG_INT = -9;
    public const int TAG_SYMBOL = -8;
    public const int TAG_STRING = -7;
    public const int TAG_OBJECT = -1;
    public const int TAG_INT = 0;
    public const int TAG_BOOL = 1;
    public const int TAG_NULL = 2;
    public const int TAG_UNDEFINED = 3;
    public const int TAG_EXCEPTION = 6;
    public const int TAG_FLOAT64 = 8;

    public bool IsNumber => Tag == TAG_INT || Tag >= TAG_FLOAT64;
    public bool IsBool => Tag == TAG_BOOL;
    public bool IsNull => Tag == TAG_NULL;
    public bool IsUndefined => Tag == TAG_UNDEFINED;
    public bool IsException => Tag == TAG_EXCEPTION;
    public bool IsString => Tag == TAG_STRING;
    public bool IsObject => Tag == TAG_OBJECT;
    public bool IsNullOrUndefined => Tag == TAG_NULL || Tag == TAG_UNDEFINED;
}

/// <summary>
/// P/Invoke declarations for quickjs.dll
/// </summary>
public static partial class QuickJSNative
{
    private const string DLL_NAME = "quickjs";

    // C function callback delegate
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate JSValue JSCFunction(IntPtr ctx, JSValue thisVal,
        int argc, IntPtr argv);

    // Log callback delegate
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LogCallback(int level,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string msg);

    // Module loader callback delegate
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int ModuleLoaderCallback(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string moduleName,
        IntPtr outBuf, int outBufSize);

    // ============= Runtime & Context =============
    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial IntPtr QJS_NewRuntime();

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_FreeRuntime(IntPtr rt);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_SetMemoryLimit(IntPtr rt, nuint limit);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_SetMaxStackSize(IntPtr rt, nuint stackSize);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_SetGCThreshold(IntPtr rt, nuint gcThreshold);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_RunGC(IntPtr rt);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_SetRuntimeOpaque(IntPtr rt, IntPtr opaque);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial IntPtr QJS_GetRuntimeOpaque(IntPtr rt);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial IntPtr QJS_NewContext(IntPtr rt);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_FreeContext(IntPtr ctx);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_SetContextOpaque(IntPtr ctx, IntPtr opaque);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial IntPtr QJS_GetContextOpaque(IntPtr ctx);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial IntPtr QJS_GetRuntime(IntPtr ctx);

    // ============= Evaluation =============
    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_Eval(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string input,
        nuint inputLen,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filename,
        int evalFlags);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_EvalFunction(IntPtr ctx, JSValue funObj);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_DetectModule(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string input, nuint inputLen);

    // ============= Value Creation =============
    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_NewNull();

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_NewUndefined();

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_NewBool(IntPtr ctx, int val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_NewInt32(IntPtr ctx, int val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_NewInt64(IntPtr ctx, long val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_NewFloat64(IntPtr ctx, double val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_NewString(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string str);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_NewStringLen(IntPtr ctx, IntPtr str, nuint len);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_NewObject(IntPtr ctx);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_NewArray(IntPtr ctx);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_NewArrayBufferCopy(IntPtr ctx, IntPtr buf, nuint len);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_NewError(IntPtr ctx);

    // ============= Value Type Checking =============
    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_IsNumber(JSValue val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_IsBool(JSValue val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_IsNull(JSValue val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_IsUndefined(JSValue val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_IsException(JSValue val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_IsString(JSValue val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_IsObject(JSValue val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_IsArray(IntPtr ctx, JSValue val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_IsFunction(IntPtr ctx, JSValue val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_IsError(IntPtr ctx, JSValue val);

    // ============= Value Extraction =============
    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_ToInt32(IntPtr ctx, out int pres, JSValue val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_ToInt64(IntPtr ctx, out long pres, JSValue val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_ToFloat64(IntPtr ctx, out double pres, JSValue val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_ToBool(IntPtr ctx, JSValue val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial IntPtr QJS_ToCString(IntPtr ctx, JSValue val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial IntPtr QJS_ToCStringLen(IntPtr ctx, out nuint plen, JSValue val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_FreeCString(IntPtr ctx, IntPtr ptr);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial IntPtr QJS_GetArrayBuffer(IntPtr ctx, out nuint psize, JSValue obj);

    // ============= Reference Counting =============
    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_DupValue(IntPtr ctx, JSValue val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_FreeValue(IntPtr ctx, JSValue val);

    // ============= Object Properties =============
    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_GetPropertyStr(IntPtr ctx, JSValue thisObj,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string prop);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_SetPropertyStr(IntPtr ctx, JSValue thisObj,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string prop, JSValue val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_GetPropertyUint32(IntPtr ctx, JSValue thisObj, uint idx);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_SetPropertyUint32(IntPtr ctx, JSValue thisObj,
        uint idx, JSValue val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_DeletePropertyStr(IntPtr ctx, JSValue thisObj,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string prop);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_DefinePropertyValueStr(IntPtr ctx, JSValue thisObj,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string prop, JSValue val, int flags);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_GetGlobalObject(IntPtr ctx);

    // ============= Function Calling =============
    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_Call(IntPtr ctx, JSValue funcObj,
        JSValue thisObj, int argc, IntPtr argv);

    // ============= C Function Registration =============
    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_NewCFunction(IntPtr ctx, IntPtr func,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int length);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_SetPropertyFunctionStr(IntPtr ctx, JSValue obj,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, IntPtr func, int length);

    // ============= Error Handling =============
    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_Throw(IntPtr ctx, JSValue obj);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_GetException(IntPtr ctx);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_HasException(IntPtr ctx);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_ThrowTypeError(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string msg);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_ThrowReferenceError(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string msg);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_ThrowInternalError(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string msg);

    // ============= JSON =============
    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_ParseJSON(IntPtr ctx,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string buf, nuint bufLen,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filename);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_JSONStringify(IntPtr ctx, JSValue obj);

    // ============= Promise & Jobs =============
    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_IsJobPending(IntPtr rt);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_ExecutePendingJob(IntPtr rt, out IntPtr pctx);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_NewPromiseCapability(IntPtr ctx,
        [Out] JSValue[] resolvingFuncs);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_PromiseState(IntPtr ctx, JSValue promise);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_PromiseResult(IntPtr ctx, JSValue promise);

    // ============= Standard Library =============
    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_StdAddHelpers(IntPtr ctx);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_StdInitHandlers(IntPtr rt);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_StdFreeHandlers(IntPtr rt);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_StdLoop(IntPtr ctx);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial JSValue QJS_StdAwait(IntPtr ctx, JSValue obj);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_StdDumpError(IntPtr ctx);

    // ============= Console Log Callback =============
    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_SetLogCallback(IntPtr callback);

    // ============= Intrinsics =============
    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_AddIntrinsicBaseObjects(IntPtr ctx);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_AddIntrinsicDate(IntPtr ctx);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_AddIntrinsicEval(IntPtr ctx);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_AddIntrinsicJSON(IntPtr ctx);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_AddIntrinsicProxy(IntPtr ctx);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_AddIntrinsicMapSet(IntPtr ctx);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_AddIntrinsicTypedArrays(IntPtr ctx);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_AddIntrinsicPromise(IntPtr ctx);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_AddIntrinsicRegExp(IntPtr ctx);

    // ============= Additional Helpers =============
    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial IntPtr QJS_GetVersion();

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int QJS_ValueGetTag(JSValue val);

    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_FreeArrayBuffer(IntPtr ctx, IntPtr ptr);

    // ============= Module Loader =============
    [LibraryImport(DLL_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void QJS_SetModuleLoaderFunc(IntPtr rt, IntPtr loaderCb);

    // ============= Eval flags =============
    public const int JS_EVAL_TYPE_GLOBAL = 0;
    public const int JS_EVAL_TYPE_MODULE = 1;
    public const int JS_EVAL_FLAG_STRICT = (1 << 3);
    public const int JS_EVAL_FLAG_COMPILE_ONLY = (1 << 5);
    public const int JS_EVAL_FLAG_ASYNC = (1 << 7);

    // ============= Property flags =============
    public const int JS_PROP_CONFIGURABLE = (1 << 0);
    public const int JS_PROP_WRITABLE = (1 << 1);
    public const int JS_PROP_ENUMERABLE = (1 << 2);
    public const int JS_PROP_C_W_E = JS_PROP_CONFIGURABLE | JS_PROP_WRITABLE | JS_PROP_ENUMERABLE;
}
