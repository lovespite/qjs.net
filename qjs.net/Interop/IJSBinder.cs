using QuickJSNet.Bindings;
using QuickJsNet.Core;

namespace QuickJsNet.Interop;

/// <summary>
/// Strongly-typed binder produced by the QuickJsNet source generator for a
/// <see cref="JSExportAttribute"/>-marked CLR type. One implementation is
/// generated per exported type and registered in <see cref="JSBinderRegistry"/>
/// at module init.
/// <para>
/// The binder owns the per-type prototype object construction and knows how to
/// wrap/unwrap instances. All fast-path JS callbacks (property accessors,
/// methods, indexers, events) are emitted as <c>[UnmanagedCallersOnly]</c>
/// static methods on the generated binder type, so the hot path is fully
/// AOT-safe and reflection-free.
/// </para>
/// </summary>
public interface IJSBinder
{
    /// <summary>The CLR type this binder targets.</summary>
    Type TargetType { get; }

    /// <summary>
    /// Wrap a managed instance as a <see cref="JSValue"/> rooted in
    /// <paramref name="runtime"/>. The returned value owns one reference and
    /// must be freed by the caller.
    /// </summary>
    JSValue Wrap(QuickJSRuntime runtime, object target);

    /// <summary>
    /// Build a JS object representing the type's static container
    /// (analogous to a JS class object) bound to the given runtime. Holds the
    /// type's static properties and methods.
    /// </summary>
    JSValue BuildStaticContainer(QuickJSRuntime runtime);
}

/// <inheritdoc />
public interface IJSBinder<T> : IJSBinder where T : class
{
    /// <summary>Strongly-typed wrap entrypoint.</summary>
    JSValue Wrap(QuickJSRuntime runtime, T target);
}
