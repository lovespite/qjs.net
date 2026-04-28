using System.Collections.Concurrent;

namespace QuickJsNet.Interop;

/// <summary>
/// Process-wide registry mapping CLR types to their generated
/// <see cref="IJSBinder"/>. Populated at module init via the
/// <see cref="System.Runtime.CompilerServices.ModuleInitializerAttribute"/>
/// emitted by the source generator – AOT-safe, no reflection, no scanning.
/// </summary>
public static class JSBinderRegistry
{
    private static readonly ConcurrentDictionary<Type, IJSBinder> _binders = new();

    /// <summary>Register a binder for a type. Idempotent (last-wins).</summary>
    public static void Register(Type type, IJSBinder binder)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(binder);
        _binders[type] = binder;
    }

    /// <summary>Try to resolve the binder for a type.</summary>
    public static bool TryGet(Type type, out IJSBinder? binder)
        => _binders.TryGetValue(type, out binder!);

    /// <summary>Strongly-typed lookup for hot paths.</summary>
    public static IJSBinder<T>? Get<T>() where T : class
        => _binders.TryGetValue(typeof(T), out var b) ? (IJSBinder<T>)b : null;

    /// <summary>Returns true if any binder is registered for the type.</summary>
    public static bool IsRegistered(Type type) => _binders.ContainsKey(type);
}
