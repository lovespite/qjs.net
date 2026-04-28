namespace QuickJsNet.Interop;

/// <summary>
/// Overrides the JavaScript-side name of an exported member.
/// Without this attribute, names are converted from PascalCase to camelCase.
/// </summary>
[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Method |
    AttributeTargets.Event,
    Inherited = false, AllowMultiple = false)]
public sealed class JSNameAttribute : Attribute
{
    public string Name { get; }
    public JSNameAttribute(string name) { Name = name; }
}
