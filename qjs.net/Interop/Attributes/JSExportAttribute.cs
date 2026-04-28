namespace QuickJsNet.Interop;

/// <summary>
/// Marks a class as exportable to JavaScript via the QuickJsNet source generator.
/// At compile time, the generator emits a strongly-typed binder
/// (<see cref="IJSBinder{T}"/>) that wraps instances of the marked type as JS
/// objects. The wrapper exposes public instance properties, methods, indexers,
/// events and static members – all without runtime reflection.
/// <para>
/// The marked class must be <c>partial</c>. Members may opt out via
/// <see cref="JSIgnoreAttribute"/> or rename via <see cref="JSNameAttribute"/>.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class JSExportAttribute : Attribute
{
    /// <summary>
    /// Optional JavaScript-side type name (used when registering the
    /// constructor/static container with <c>engine.SetGlobal</c>). Defaults to
    /// the C# type name.
    /// </summary>
    public string? Name { get; }

    /// <summary>Default ctor – uses the C# type name on the JS side.</summary>
    public JSExportAttribute() { }

    /// <summary>Specify an explicit JS-side type name.</summary>
    public JSExportAttribute(string name) { Name = name; }
}
