using QuickJSNet.Bindings;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace QuickJsNet.Core;

public partial class QuickJSRuntime
{
    private ModuleLoader? _moduleLoader;

    /// <summary>Module loader for ES module (<c>import</c>/<c>export</c>) resolution.</summary>
    public ModuleLoader Modules
    {
        get
        {
            if (_moduleLoader is null)
            {
                _moduleLoader = new ModuleLoader();
                _moduleLoader.OnNativeRegistered = InstallNativeModule;
            }
            return _moduleLoader;
        }
    }

    /// <summary>
    /// Install the native module loader callback. Idempotent across runtimes —
    /// the underlying <c>g_module_loader_cb</c> is process-global, but each
    /// invocation is routed back to the correct managed runtime via the
    /// <c>ctx</c> parameter using <see cref="FromContext"/>.
    /// </summary>
    private static void InstallModuleLoader(IntPtr runtime)
    {
        unsafe
        {
            IntPtr cb = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, int, int>)
                &ModuleLoaderTrampoline;
            QuickJSNative.QJS_SetModuleLoaderFunc(runtime, cb);

            IntPtr urlCb = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, int, int>)
                &ModuleUrlTrampoline;
            QuickJSNative.QJS_SetModuleUrlFunc(runtime, urlCb);
        }
    }

    private sealed class PendingFetch
    {
        public string Name = string.Empty;
        public byte[] Bytes = [];
        public byte[]? UrlBytes;
    }

    [ThreadStatic]
    private static PendingFetch? _pendingFetch;

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static int ModuleLoaderTrampoline(IntPtr ctx, IntPtr namePtr, IntPtr buf, int bufSize)
    {
        try
        {
            var rt = FromContext(ctx);
            if (rt is null) return -1;
            var loader = rt._moduleLoader;
            if (loader is null) return -1;

            var name = Marshal.PtrToStringUTF8(namePtr) ?? string.Empty;

            // Two-call protocol with native bridge:
            //   Probe (buf=0, bufSize=0): resolve source, cache UTF-8 bytes, return byte count.
            //   Fetch (buf!=0):           copy cached bytes into buf, clear cache, return count.
            if (buf == IntPtr.Zero || bufSize == 0)
            {
                _pendingFetch = null;
                var resolved = loader.ResolveDetailed(name);
                if (resolved is null) return -1;
                var bytes = Encoding.UTF8.GetBytes(resolved.Source);
                _pendingFetch = new PendingFetch
                {
                    Name = name,
                    Bytes = bytes,
                    UrlBytes = Encoding.UTF8.GetBytes(resolved.Url),
                };
                return bytes.Length;
            }

            var pending = _pendingFetch;
            if (pending is null || pending.Name != name)
            {
                // Out-of-order or stale fetch: re-resolve as a safety net.
                var resolved = loader.ResolveDetailed(name);
                if (resolved is null) return -1;
                pending = new PendingFetch
                {
                    Name = name,
                    Bytes = Encoding.UTF8.GetBytes(resolved.Source),
                    UrlBytes = Encoding.UTF8.GetBytes(resolved.Url),
                };
                _pendingFetch = pending;
            }

            var data = pending.Bytes;
            if (data.Length > bufSize) { _pendingFetch = null; return -1; }
            unsafe
            {
                fixed (byte* p = data)
                {
                    Buffer.MemoryCopy(p, (void*)buf, bufSize, data.Length);
                }
            }
            // Keep _pendingFetch alive so the URL trampoline (called by the C
            // bridge immediately after compile) can still see UrlBytes. The
            // URL trampoline clears it on its second call.
            pending.Bytes = [];
            return data.Length;
        }
        catch
        {
            _pendingFetch = null;
            return -1;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static int ModuleUrlTrampoline(IntPtr ctx, IntPtr namePtr, IntPtr buf, int bufSize)
    {
        try
        {
            var name = Marshal.PtrToStringUTF8(namePtr) ?? string.Empty;
            var pending = _pendingFetch;

            byte[]? urlBytes = null;
            if (pending is not null && pending.Name == name && pending.UrlBytes is not null)
            {
                urlBytes = pending.UrlBytes;
            }
            else
            {
                // Re-resolve URL (rare path; used if the order ever drifts).
                var rt = FromContext(ctx);
                var loader = rt?._moduleLoader;
                var resolved = loader?.ResolveDetailed(name);
                if (resolved is null) return -1;
                urlBytes = Encoding.UTF8.GetBytes(resolved.Url);
            }

            if (buf == IntPtr.Zero || bufSize == 0)
                return urlBytes.Length;

            if (urlBytes.Length > bufSize) return -1;
            unsafe
            {
                fixed (byte* p = urlBytes)
                {
                    Buffer.MemoryCopy(p, (void*)buf, bufSize, urlBytes.Length);
                }
            }
            // Done with this module's pending state.
            if (pending is not null && pending.Name == name)
                _pendingFetch = null;
            return urlBytes.Length;
        }
        catch
        {
            return -1;
        }
    }

    // ─────────────────── Native (C#-defined) modules ───────────────────

    /// <summary>
    /// Maps <c>JSModuleDef*</c> pointers to their managed builders so the
    /// init trampoline can route back to the right runtime + module.
    /// </summary>
    private static readonly ConcurrentDictionary<IntPtr, (QuickJSRuntime Rt, NativeModuleBuilder Builder)>
        _nativeModulesByDef = new();

    private void InstallNativeModule(NativeModuleBuilder builder)
    {
        IntPtr initPtr;
        unsafe
        {
            initPtr = (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int>)
                &NativeModuleInitTrampoline;
        }

        var m = QuickJSNative.QJS_NewCModule(_context, builder.Name, initPtr);
        if (m == IntPtr.Zero)
            throw new InvalidOperationException(
                $"QJS_NewCModule failed for native module '{builder.Name}'");

        foreach (var exp in builder.ExportNames)
            QuickJSNative.QJS_AddModuleExport(_context, m, exp);

        _nativeModulesByDef[m] = (this, builder);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static int NativeModuleInitTrampoline(IntPtr ctx, IntPtr m)
    {
        try
        {
            if (!_nativeModulesByDef.TryGetValue(m, out var entry))
                return -1;
            QuickJSNative.QJS_SetImportMeta(ctx, m, "qjs-native:" + entry.Builder.Name, 0);
            entry.Builder.Materialize(entry.Rt, m);
            return 0;
        }
        catch
        {
            return -1;
        }
    }
}
