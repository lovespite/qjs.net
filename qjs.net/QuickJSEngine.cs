using QuickJsNet.Core;
using QuickJsNet.Modules;
using QuickJsNet.Utils;
using QuickJSNet.Bindings;
using System.Runtime.InteropServices;

namespace QuickJsNet;

/// <summary>
/// Configuration options for <see cref="QuickJSEngine"/>.
/// </summary>
public sealed class QuickJSEngineOptions
{
    /// <summary>
    /// Maximum heap memory the JS runtime is allowed to allocate (bytes).
    /// 0 means no limit.
    /// </summary>
    public ulong MemoryLimit { get; set; }

    /// <summary>
    /// Maximum native stack size (bytes). 0 means default.
    /// </summary>
    public ulong StackSize { get; set; }

    /// <summary>Install TextEncoder / TextDecoder (default: true).</summary>
    public bool Encoder { get; set; } = true;

    /// <summary>Install synchronous <c>fs</c> module (default: true).</summary>
    public bool FileSystem { get; set; } = true;

    /// <summary>Install asynchronous <c>fsAsync</c> module (default: true).</summary>
    public bool AsyncFileSystem { get; set; } = true;

    /// <summary>Install <c>fetch()</c> function (default: true).</summary>
    public bool Fetch { get; set; } = true;

    /// <summary>Install simple Windows dialogs – alert / confirm / prompt (default: true).</summary>
    public bool WindowsDialogs { get; set; } = true;

    /// <summary>
    /// Optional fetch module configuration (proxy, SSL, timeout).
    /// Only used when <see cref="Fetch"/> is <c>true</c>.
    /// </summary>
    public FetchModuleOptions? FetchOptions { get; set; }

    /// <summary>
    /// Optional base path for file system sandboxing.
    /// When set, all fs / fsAsync operations are restricted to this directory.
    /// </summary>
    public string? FileSystemBasePath { get; set; }

    /// <summary>
    /// Path to the localStorage database file.
    /// When set, a <c>localStorage</c> object is available in JS.
    /// </summary>
    public string? LocalStoragePath { get; set; }

    /// <summary>
    /// When <c>true</c>, also expose enabled built-in modules as importable
    /// <c>qjs:*</c> ES modules (e.g. <c>import fs from 'qjs:fs'</c>).
    /// Defaults to <c>false</c> for backward compatibility.
    /// </summary>
    public bool BuiltinAsModule { get; set; }

    /// <summary>
    /// When <c>true</c> (and <see cref="BuiltinAsModule"/> is <c>true</c>),
    /// suppress global injection of built-ins so they are <strong>only</strong>
    /// reachable via <c>import</c>. Useful for sandboxed evaluation that wants
    /// explicit dependency declarations. Defaults to <c>false</c>.
    /// </summary>
    public bool BuiltinAsModuleOnly { get; set; }
}

/// <summary>
/// High-level, public facade for the QuickJS JavaScript engine.
/// <para>
/// Provides script evaluation with full async/await support, global variable
/// access, C#-to-JS function registration, and configurable built-in modules
/// (fetch, fs, TextEncoder, etc.).
/// </para>
/// <example>
/// <code>
/// using var engine = new QuickJSEngine();
/// engine.SetGlobal("name", "World");
/// var result = engine.Execute("'Hello, ' + name");
/// Console.WriteLine(result); // Hello, World
/// </code>
/// </example>
/// </summary>
public sealed class QuickJSEngine : IDisposable
{
    private readonly QuickJSRuntime _runtime;
    private bool _disposed;

    /// <summary>
    /// Create an engine with default settings (all modules enabled, no limits).
    /// </summary>
    public QuickJSEngine() : this(new QuickJSEngineOptions()) { }

    /// <summary>
    /// Create an engine by configuring options via a delegate.
    /// </summary>
    /// <example>
    /// <code>
    /// using var engine = new QuickJSEngine(opt =>
    /// {
    ///     opt.MemoryLimit = 64 * 1024 * 1024;
    ///     opt.LocalStoragePath = "store.db";
    /// });
    /// </code>
    /// </example>
    public QuickJSEngine(Action<QuickJSEngineOptions> configure)
        : this(ApplyOptions(configure)) { }

    /// <summary>
    /// Create an engine with explicit options.
    /// </summary>
    public QuickJSEngine(QuickJSEngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _runtime = new QuickJSRuntime(options.MemoryLimit, options.StackSize);
        _runtime.OnLog += (level, msg) => OnLog?.Invoke(level, msg);

        InstallModules(options);
    }

    /// <summary>
    /// Fired when the JS engine emits a log message.
    /// Parameters: (int level, string message).
    /// </summary>
    public event Action<int, string>? OnLog;

    /// <summary>
    /// Native QuickJS library version string.
    /// </summary>
    public static string Version => QuickJSRuntime.GetVersion();

    // ──────────────────── Evaluation ────────────────────

    /// <summary>
    /// Execute JavaScript code with full event-loop support
    /// (top-level <c>await</c>, <c>setTimeout</c>, <c>fetch</c>, etc.).
    /// Blocks until all async work completes.
    /// </summary>
    /// <param name="code">JavaScript source code.</param>
    /// <param name="filename">Optional filename for stack traces.</param>
    /// <returns>The result converted to a managed object, or <c>null</c>.</returns>
    /// <exception cref="QuickJSException">Thrown on JS runtime errors.</exception>
    public object? Execute(string code, string filename = "<eval>")
    {
        ThrowIfDisposed();
        return _runtime.Execute(code, filename);
    }

    /// <summary>
    /// Execute JavaScript source as an ES module. Supports <c>import</c> /
    /// <c>export</c> syntax and top-level <c>await</c>. Module specifiers are
    /// resolved through <see cref="Modules"/>.
    /// </summary>
    /// <param name="code">Module source.</param>
    /// <param name="filename">Filename used as the module's specifier (also the import.meta key for QuickJS' internal cache).</param>
    /// <returns>The settled value of the module's top-level promise (usually <c>null</c>).</returns>
    public object? ExecuteModule(string code, string filename = "<module>")
    {
        ThrowIfDisposed();
        return _runtime.ExecuteModule(code, filename);
    }

    /// <summary>
    /// Dynamically import a module by specifier and return its namespace as
    /// a <c>Dictionary&lt;string, object?&gt;</c>. Resolution goes through
    /// <see cref="Modules"/>.
    /// </summary>
    public object? Import(string specifier)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrEmpty(specifier);
        var escaped = "\"" + specifier.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        // Stash result into a unique global because (a) module namespace objects don't JSON-serialize
        // their exports and (b) Execute returns the Promise (which JSON-serializes to "{}") rather
        // than its resolved value. The event loop still pumps, so by the time Execute returns the
        // global has been written.
        var slot = "__qjs_import_" + Guid.NewGuid().ToString("N");
        var script = $"(async () => {{ const m = await import({escaped}); const o = {{}}; const ks = Object.keys(m); for (let i=0;i<ks.length;i++) o[ks[i]] = m[ks[i]]; globalThis[\"{slot}\"] = o; }})()";
        _runtime.Execute(script, "<import>");
        var result = _runtime.GetGlobal(slot);
        _runtime.Execute($"delete globalThis[\"{slot}\"]", "<import-cleanup>");
        return result;
    }

    /// <summary>
    /// Configure ES module resolution (virtual modules, base path, custom resolver).
    /// </summary>
    public ModuleLoader Modules
    {
        get { ThrowIfDisposed(); return _runtime.Modules; }
    }

    /// <summary>
    /// Evaluate JavaScript code synchronously (no event-loop pumping).
    /// Suitable for simple expressions and scripts without async operations.
    /// </summary>
    /// <param name="code">JavaScript source code.</param>
    /// <param name="filename">Optional filename for stack traces.</param>
    /// <param name="asModule">If <c>true</c>, evaluate as an ES module.</param>
    /// <returns>The result converted to a managed object, or <c>null</c>.</returns>
    /// <exception cref="QuickJSException">Thrown on JS runtime errors.</exception>
    public object? Eval(string code, string filename = "<eval>", bool asModule = false)
    {
        ThrowIfDisposed();
        return _runtime.Eval(code, filename, asModule);
    }

    // ──────────────────── Function Invocation ────────────────────

    /// <summary>
    /// Invoke a global JavaScript function by name.
    /// Supports async functions – the event loop is pumped until the returned
    /// promise settles.
    /// </summary>
    /// <param name="functionName">Name of the global JS function.</param>
    /// <param name="args">Arguments (automatically converted to JS values).</param>
    /// <returns>The return value converted to a managed object, or <c>null</c>.</returns>
    /// <exception cref="QuickJSException">Thrown when the function doesn't exist or throws.</exception>
    public object? Invoke(string functionName, params object[] args)
    {
        ThrowIfDisposed();
        return _runtime.InvokeFunction(functionName, args);
    }

    /// <summary>
    /// Check whether a global function with the given name exists.
    /// </summary>
    public bool HasFunction(string functionName)
    {
        ThrowIfDisposed();
        return _runtime.HasFunction(functionName);
    }

    // ──────────────────── Global Variables ────────────────────

    /// <summary>
    /// Set a global variable visible to all subsequently executed scripts.
    /// Supported value types: <c>null</c>, <c>bool</c>, <c>int</c>, <c>long</c>,
    /// <c>float</c>, <c>double</c>, <c>string</c>, <c>byte[]</c>.
    /// </summary>
    public void SetGlobal(string name, object? value)
    {
        ThrowIfDisposed();
        _runtime.SetGlobal(name, value!);
    }

    /// <summary>
    /// Install the static container for a <c>[JSExport]</c>-annotated type as
    /// a global JS object (its static methods/properties become accessible as
    /// <c>name.staticMember</c> in JS). AOT-safe.
    /// </summary>
    public void SetGlobalStatic<T>(string name) where T : class
    {
        ThrowIfDisposed();
        _runtime.SetGlobalStatic<T>(name);
    }

    /// <summary>
    /// Read a global variable. Objects and arrays are returned as JSON strings.
    /// </summary>
    public object? GetGlobal(string name)
    {
        ThrowIfDisposed();
        return _runtime.GetGlobal(name);
    }

    /// <summary>
    /// Indexer shorthand for <see cref="GetGlobal"/> / <see cref="SetGlobal"/>.
    /// <code>
    /// engine["greeting"] = "Hello";
    /// var v = engine["greeting"]; // "Hello"
    /// </code>
    /// </summary>
    public object? this[string name]
    {
        get => GetGlobal(name);
        set => SetGlobal(name, value);
    }

    // ──────────────────── Function Registration ────────────────────

    /// <summary>
    /// Register a C# function as a global JavaScript function.
    /// Arguments and return values are automatically converted between
    /// managed types and JS values.
    /// </summary>
    /// <param name="name">Name to expose in JS global scope.</param>
    /// <param name="handler">
    /// Handler receiving an array of managed arguments
    /// (<c>null</c>, <c>bool</c>, <c>int</c>, <c>double</c>, <c>string</c>, …)
    /// and returning a managed value (or <c>null</c>).
    /// </param>
    /// <param name="argCount">Expected argument count (used for JS <c>.length</c>).</param>
    /// <example>
    /// <code>
    /// engine.RegisterFunction("add", args =>
    /// {
    ///     double a = Convert.ToDouble(args[0]);
    ///     double b = Convert.ToDouble(args[1]);
    ///     return a + b;
    /// }, argCount: 2);
    ///
    /// engine.Execute("add(1, 2)"); // 3
    /// </code>
    /// </example>
    public void RegisterFunction(string name, Func<object?[], object?> handler, int argCount = 0)
    {
        ThrowIfDisposed();
        _runtime.RegisterGlobalFunction(name, jsArgs =>
        {
            var managedArgs = new object?[jsArgs.Length];
            for (int i = 0; i < jsArgs.Length; i++)
                managedArgs[i] = _runtime.JSValueToManaged(jsArgs[i]);
            return handler(managedArgs);
        }, argCount);
    }

    /// <summary>
    /// Register a parameterless C# action as a global JavaScript function.
    /// </summary>
    public void RegisterFunction(string name, Action handler)
    {
        ThrowIfDisposed();
        _runtime.RegisterGlobalFunction(name, _ =>
        {
            handler();
            return null;
        }, 0);
    }

    /// <summary>
    /// Register a C# action (with managed arguments) as a global JavaScript function.
    /// </summary>
    public void RegisterFunction(string name, Action<object?[]> handler, int argCount = 0)
    {
        ThrowIfDisposed();
        _runtime.RegisterGlobalFunction(name, jsArgs =>
        {
            var managedArgs = new object?[jsArgs.Length];
            for (int i = 0; i < jsArgs.Length; i++)
                managedArgs[i] = _runtime.JSValueToManaged(jsArgs[i]);
            handler(managedArgs);
            return null;
        }, argCount);
    }

    /// <summary>
    /// Register a delegate (Func or Action) as a global JavaScript function.
    /// </summary>
    /// <param name="name"></param>
    /// <param name="del"></param>
    public void RegisterFunction(string name, Delegate del)
    {
        ThrowIfDisposed();
        _runtime.RegisterGlobalFunction(name, jsArgs =>
        {
            var managedArgs = new object?[jsArgs.Length];
            for (int i = 0; i < jsArgs.Length; i++)
                managedArgs[i] = _runtime.JSValueToManaged(jsArgs[i]);
            return del.DynamicInvoke(managedArgs);
        }, del.Method.GetParameters().Length);
    }

    // ──────────────────── Utilities ────────────────────

    /// <summary>
    /// Force a garbage-collection cycle inside the JS heap.
    /// </summary>
    public void RunGC()
    {
        ThrowIfDisposed();
        _runtime.RunGC();
    }

    /// <summary>
    /// Drain pending timers, queued callbacks and microtasks once.
    /// Non-blocking; intended to be called from a host's per-frame tick so that
    /// <c>setTimeout</c> / <c>setInterval</c> callbacks fire while the engine is otherwise idle.
    /// Must be invoked on the JS thread.
    /// </summary>
    public void PumpEventLoop()
    {
        ThrowIfDisposed();
        _runtime.PumpEventLoop();
    }

    // ──────────────────── Dispose ────────────────────

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _runtime.Dispose();
    }

    // ──────────────────── Private Helpers ────────────────────

    private static QuickJSEngineOptions ApplyOptions(Action<QuickJSEngineOptions> configure)
    {
        var options = new QuickJSEngineOptions();
        configure(options);
        return options;
    }

    private void InstallModules(QuickJSEngineOptions options)
    {
        // Buffer must always be available because other modules accept it as
        // an alternative to ArrayBuffer/Uint8Array. Installing it also runs
        // its static constructor, which wires the BufferUnwrapHook used by
        // [JSExport]-generated `byte[]` parameter readers.
        System.Runtime.CompilerServices.RuntimeHelpers
            .RunClassConstructor(typeof(QuickJsNet.Modules.Buffer).TypeHandle);
        _runtime.SetGlobalStatic<QuickJsNet.Modules.Buffer>("Buffer");
        _runtime.SetGlobalStatic<QuickJsNet.Modules.Stream>("Stream");

        if (options.Encoder)
            EncoderModule.Install(_runtime);

        if (options.FileSystem)
            _runtime.SetGlobal("fs", new FileSystemModule(options.FileSystemBasePath));

        if (options.AsyncFileSystem)
            _runtime.SetGlobal("fsAsync", new AsyncFileSystemModule(options.FileSystemBasePath));

        if (options.Fetch)
            FetchModule.Install(_runtime, options.FetchOptions);

        if (options.WindowsDialogs)
            WindowsSimple.Install(_runtime);

        if (!string.IsNullOrEmpty(options.LocalStoragePath))
            _runtime.InstallLocalStorageModule(options.LocalStoragePath);

        if (options.BuiltinAsModule)
        {
            BuiltinModuleBridge.Install(_runtime, options);
            if (options.BuiltinAsModuleOnly)
                BuiltinModuleBridge.RemoveGlobals(_runtime, options);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
