namespace QuickJsNet.Interop;

/// <summary>
/// Excludes a member of a <see cref="JSExportAttribute"/>-marked type from
/// being exposed to JavaScript. May be applied to properties, methods,
/// events and indexers.
/// </summary>
[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Method |
    AttributeTargets.Event | AttributeTargets.Field,
    Inherited = false, AllowMultiple = false)]
public sealed class JSIgnoreAttribute : Attribute { }
