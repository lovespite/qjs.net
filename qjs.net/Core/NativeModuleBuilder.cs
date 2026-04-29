using System.Runtime.InteropServices;
using QuickJSNet.Bindings;

namespace QuickJsNet.Core;

/// <summary>
/// Builder used by <see cref="ModuleLoader.RegisterNative"/> to declare exports
/// for a C#-defined ES module. The builder records export names eagerly (so
/// importer compilation can resolve them) and defers value materialization to
/// the module's <em>init</em> phase, when the JS engine actually evaluates
/// <c>import</c>s.
/// </summary>
public sealed class NativeModuleBuilder
{
    internal const string DefaultExportName = "default";

    private readonly Dictionary<string, Func<QuickJSRuntime, JSValue>> _exports =
        new(StringComparer.Ordinal);

    /// <summary>The module specifier (e.g. <c>"qjs:fs"</c>).</summary>
    public string Name { get; }

    internal NativeModuleBuilder(string name) { Name = name; }

    /// <summary>Names of all declared exports, in declaration order.</summary>
    public IEnumerable<string> ExportNames => _exports.Keys;

    /// <summary>Number of declared exports.</summary>
    public int Count => _exports.Count;

    /// <summary>Export an arbitrary managed value (converted via <c>ManagedToJSValue</c>).</summary>
    public NativeModuleBuilder Export(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _exports[name] = rt => rt.ManagedToJSValue(value);
        return this;
    }

    /// <summary>Export a factory whose value is materialized when the module is first imported.</summary>
    public NativeModuleBuilder ExportFactory(string name, Func<QuickJSRuntime, object?> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(factory);
        _exports[name] = rt => rt.ManagedToJSValue(factory(rt));
        return this;
    }

    /// <summary>
    /// Export a raw <see cref="JSValue"/> produced by <paramref name="factory"/>.
    /// Use this when you need to surface a JS-native object (e.g. a function
    /// pointer or a binder-built static container) without going through
    /// managed → JS coercion.
    /// </summary>
    public NativeModuleBuilder ExportRaw(string name, Func<QuickJSRuntime, JSValue> factory)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(factory);
        _exports[name] = factory;
        return this;
    }

    /// <summary>Export a JS function backed by a managed delegate.</summary>
    public NativeModuleBuilder ExportFunc(string name, Delegate handler, int argCount = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(handler);
        _exports[name] = rt => rt.MakeFunctionValue(name, handler, argCount);
        return this;
    }

    /// <summary>Export the module's default value.</summary>
    public NativeModuleBuilder ExportDefault(object? value)
        => Export(DefaultExportName, value);

    /// <summary>Export a default factory.</summary>
    public NativeModuleBuilder ExportDefaultFactory(Func<QuickJSRuntime, object?> factory)
        => ExportFactory(DefaultExportName, factory);

    /// <summary>Export a default function.</summary>
    public NativeModuleBuilder ExportDefaultFunc(Delegate handler, int argCount = 0)
        => ExportFunc(DefaultExportName, handler, argCount);

    internal void Materialize(QuickJSRuntime rt, IntPtr moduleDef)
    {
        foreach (var kv in _exports)
        {
            var val = kv.Value(rt);
            QuickJSNative.QJS_SetModuleExport(rt.Context, moduleDef, kv.Key, val);
        }
    }
}
