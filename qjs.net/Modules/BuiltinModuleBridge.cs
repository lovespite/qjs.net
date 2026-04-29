using QuickJsNet.Core;
using QuickJSNet.Bindings;

namespace QuickJsNet.Modules;

/// <summary>
/// Re-exposes built-in modules (fs, fetch, Buffer, ...) as importable
/// <c>qjs:*</c> ES modules. Each module's <c>default</c> export is the same
/// JS-side value that would otherwise live on <c>globalThis</c>.
/// </summary>
internal static class BuiltinModuleBridge
{
    /// <summary>
    /// Map from native module name to the global property it mirrors.
    /// Keep in sync with <see cref="QuickJSEngine.InstallModules"/>.
    /// </summary>
    private static readonly (string Module, string Global)[] Mappings =
    {
        ("qjs:buffer",          "Buffer"),
        ("qjs:stream",          "Stream"),
        ("qjs:fs",              "fs"),
        ("qjs:fsAsync",         "fsAsync"),
        ("qjs:fetch",           "fetch"),
        ("qjs:textEncoder",     "TextEncoder"),
        ("qjs:textDecoder",     "TextDecoder"),
        ("qjs:dialogs",         null!), // composite: alert/confirm/prompt
    };

    public static void Install(QuickJSRuntime runtime, QuickJSEngineOptions options)
    {
        // Buffer/Stream are always installed.
        Bridge(runtime, "qjs:buffer", "Buffer");
        Bridge(runtime, "qjs:stream", "Stream");

        if (options.FileSystem)
            Bridge(runtime, "qjs:fs", "fs");
        if (options.AsyncFileSystem)
            Bridge(runtime, "qjs:fsAsync", "fsAsync");
        if (options.Fetch)
            Bridge(runtime, "qjs:fetch", "fetch");
        if (options.Encoder)
        {
            Bridge(runtime, "qjs:textEncoder", "TextEncoder");
            Bridge(runtime, "qjs:textDecoder", "TextDecoder");
        }
        if (options.WindowsDialogs)
            BridgeDialogs(runtime);
    }

    /// <summary>
    /// Register a single qjs:* module whose <c>default</c> export mirrors the
    /// JS value at <c>globalThis[<paramref name="globalKey"/>]</c>.
    /// The value is captured (and duped) <em>eagerly</em> at registration time,
    /// so removing the global afterwards (via <c>BuiltinAsModuleOnly</c>) does
    /// not invalidate the import.
    /// </summary>
    private static void Bridge(QuickJSRuntime rt, string moduleName, string globalKey)
    {
        var captured = GetGlobalAsJSValue(rt, globalKey); // refcount +1, owned by us
        rt.Modules.RegisterNative(moduleName, b => b
            .ExportRaw(NativeModuleBuilder.DefaultExportName,
                r => QuickJSNative.QJS_DupValue(r.Context, captured)));
        // Note: 'captured' leaks one refcount on context teardown, which is
        // released along with the runtime — safe.
    }

    private static void BridgeDialogs(QuickJSRuntime rt)
    {
        var alert   = GetGlobalAsJSValue(rt, "alert");
        var confirm = GetGlobalAsJSValue(rt, "confirm");
        var prompt  = GetGlobalAsJSValue(rt, "prompt");
        rt.Modules.RegisterNative("qjs:dialogs", b => b
            .ExportRaw("alert",   r => QuickJSNative.QJS_DupValue(r.Context, alert))
            .ExportRaw("confirm", r => QuickJSNative.QJS_DupValue(r.Context, confirm))
            .ExportRaw("prompt",  r => QuickJSNative.QJS_DupValue(r.Context, prompt)));
    }

    private static JSValue GetGlobalAsJSValue(QuickJSRuntime rt, string name)
    {
        var ctx = rt.Context;
        var global = QuickJSNative.QJS_GetGlobalObject(ctx);
        try
        {
            var v = QuickJSNative.QJS_GetPropertyStr(ctx, global, name);
            return v;
        }
        finally
        {
            QuickJSNative.QJS_FreeValue(ctx, global);
        }
    }

    /// <summary>
    /// Remove a previously installed global. Used by <c>BuiltinAsModuleOnly</c>
    /// to suppress global pollution after an ESM bridge has been registered.
    /// </summary>
    public static void RemoveGlobals(QuickJSRuntime rt, QuickJSEngineOptions options)
    {
        var ctx = rt.Context;
        var global = QuickJSNative.QJS_GetGlobalObject(ctx);
        try
        {
            QuickJSNative.QJS_DeletePropertyStr(ctx, global, "Buffer");
            QuickJSNative.QJS_DeletePropertyStr(ctx, global, "Stream");
            if (options.FileSystem)      QuickJSNative.QJS_DeletePropertyStr(ctx, global, "fs");
            if (options.AsyncFileSystem) QuickJSNative.QJS_DeletePropertyStr(ctx, global, "fsAsync");
            if (options.Fetch)           QuickJSNative.QJS_DeletePropertyStr(ctx, global, "fetch");
            if (options.Encoder)
            {
                QuickJSNative.QJS_DeletePropertyStr(ctx, global, "TextEncoder");
                QuickJSNative.QJS_DeletePropertyStr(ctx, global, "TextDecoder");
            }
            if (options.WindowsDialogs)
            {
                QuickJSNative.QJS_DeletePropertyStr(ctx, global, "alert");
                QuickJSNative.QJS_DeletePropertyStr(ctx, global, "confirm");
                QuickJSNative.QJS_DeletePropertyStr(ctx, global, "prompt");
            }
        }
        finally
        {
            QuickJSNative.QJS_FreeValue(ctx, global);
        }
    }
}
