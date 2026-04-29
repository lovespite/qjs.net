using System.Collections.Concurrent;

namespace QuickJsNet.Core;

/// <summary>
/// Result of a module resolution: the source code plus the URL that should be
/// exposed via <c>import.meta.url</c>.
/// </summary>
public sealed record ResolvedModule(string Source, string Url);

/// <summary>
/// Resolves ES module specifiers to source code.
/// <para>
/// Resolution order:
/// <list type="number">
///   <item><description>Custom <see cref="Resolver"/> delegate (if provided and returns non-null).</description></item>
///   <item><description>Virtual sources registered via <see cref="Register"/>.</description></item>
///   <item><description>File system lookup under <see cref="BasePath"/>. The specifier
///     is treated as a relative path; it must remain inside <see cref="BasePath"/>.</description></item>
/// </list>
/// </para>
/// Module names like <c>./util.js</c> arrive already normalized by QuickJS
/// (e.g. joined against the importing module's path), so the loader sees the
/// full relative path from the project root.
/// </summary>
public sealed class ModuleLoader
{
    private readonly ConcurrentDictionary<string, string> _virtual =
        new(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, NativeModuleBuilder> _native =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Hook invoked by the owning <see cref="QuickJSRuntime"/> when a native
    /// module is registered, so that <c>QJS_NewCModule</c> + export name
    /// declarations can run eagerly on the JS thread.
    /// </summary>
    internal Action<NativeModuleBuilder>? OnNativeRegistered { get; set; }

    /// <summary>Sandbox root for filesystem-based module loading.</summary>
    public string? BasePath { get; set; }

    /// <summary>
    /// Optional custom resolver. Receives the (already normalized) specifier
    /// and returns the module source, or <c>null</c> to fall through to the
    /// built-in resolution chain.
    /// </summary>
    public Func<string, string?>? Resolver { get; set; }

    /// <summary>
    /// Register an in-memory virtual module. Subsequent
    /// <c>import "name"</c> calls returning this exact specifier (or one that
    /// QuickJS normalizes to it) will receive <paramref name="source"/>.
    /// </summary>
    public void Register(string name, string source)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(source);
        _virtual[name] = source;
    }

    /// <summary>Remove a previously registered virtual module.</summary>
    public bool Unregister(string name) => _virtual.TryRemove(name, out _);

    /// <summary>
    /// Register a C#-defined ES module. Exports declared by <paramref name="configure"/>
    /// become importable as <c>import { x } from "name"</c>. Export names are
    /// committed to the JS engine eagerly (so importer compilation succeeds);
    /// values are materialized lazily when the module is first imported.
    /// </summary>
    public NativeModuleBuilder RegisterNative(string name, Action<NativeModuleBuilder> configure)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(configure);
        var b = new NativeModuleBuilder(name);
        configure(b);
        _native[name] = b;
        OnNativeRegistered?.Invoke(b);
        return b;
    }

    /// <summary>Look up a previously registered native module (for the runtime bridge).</summary>
    internal NativeModuleBuilder? GetNative(string name)
        => _native.TryGetValue(name, out var b) ? b : null;

    /// <summary>All registered native module specifiers.</summary>
    public IEnumerable<string> NativeModuleNames => _native.Keys;

    /// <summary>Whether any module sources are registered or a base path / resolver is configured.</summary>
    public bool IsActive =>
        Resolver is not null || BasePath is not null || !_virtual.IsEmpty || !_native.IsEmpty;

    /// <summary>
    /// Resolve a module specifier to source code. Returns <c>null</c> when the
    /// module cannot be found (causing the import to fail).
    /// </summary>
    public string? Resolve(string specifier)
        => ResolveDetailed(specifier)?.Source;

    /// <summary>
    /// Resolve a module specifier to source code <em>and</em> the URL that
    /// should be exposed as <c>import.meta.url</c>. Returns <c>null</c> when
    /// the module cannot be found.
    /// </summary>
    /// <remarks>
    /// URL conventions:
    /// <list type="bullet">
    ///   <item><description><c>file:///&lt;abs-path&gt;</c> for filesystem-resolved modules.</description></item>
    ///   <item><description><c>qjs-virtual:&lt;name&gt;</c> for sources installed via <see cref="Register"/>.</description></item>
    ///   <item><description>Whatever the custom <see cref="Resolver"/> returns
    ///     (defaults to <c>qjs-resolver:&lt;name&gt;</c>).</description></item>
    /// </list>
    /// </remarks>
    public ResolvedModule? ResolveDetailed(string specifier)
    {
        if (string.IsNullOrEmpty(specifier)) return null;

        if (Resolver is not null)
        {
            var custom = Resolver(specifier);
            if (custom is not null)
                return new ResolvedModule(custom, "qjs-resolver:" + specifier);
        }

        if (_virtual.TryGetValue(specifier, out var virt))
            return new ResolvedModule(virt, "qjs-virtual:" + specifier);

        if (BasePath is not null)
        {
            try
            {
                var baseNorm = Path.GetFullPath(BasePath);
                var full = Path.GetFullPath(Path.Combine(baseNorm, specifier));
                if (!full.StartsWith(baseNorm, StringComparison.OrdinalIgnoreCase))
                    return null; // sandbox escape
                if (File.Exists(full))
                {
                    var src = File.ReadAllText(full);
                    var url = "file:///" + full.Replace('\\', '/');
                    return new ResolvedModule(src, url);
                }
            }
            catch
            {
                return null;
            }
        }

        return null;
    }
}
