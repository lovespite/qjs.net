using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace QuickJsNet.Interop;

/// <summary>
/// Process-wide table mapping integer ids to managed objects, used to bridge
/// JS-side opaque references back to CLR instances without any per-call
/// reflection. Each wrapped object gets a unique id stored as a hidden
/// property on the JS wrapper; getters/setters/methods read it via
/// <see cref="JSObjectTable.Get"/>.
/// <para>
/// A <see cref="GCHandle"/> keeps the managed instance alive for the lifetime
/// of the JS wrapper. Callers must <see cref="Release"/> the id when the JS
/// wrapper is no longer needed (we cannot piggy-back on QuickJS finalizers
/// without binding <c>JS_NewClass</c>; for now release happens during
/// runtime disposal).
/// </para>
/// </summary>
public static class JSObjectTable
{
    private static long _nextId;
    private static readonly ConcurrentDictionary<long, GCHandle> _table = new();

    /// <summary>Register a managed object and return its opaque id (>0).</summary>
    public static long Register(object obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        long id = Interlocked.Increment(ref _nextId);
        _table[id] = GCHandle.Alloc(obj, GCHandleType.Normal);
        return id;
    }

    /// <summary>Resolve an id back to the managed object, or null if unknown.</summary>
    public static object? Get(long id)
        => _table.TryGetValue(id, out var h) && h.IsAllocated ? h.Target : null;

    /// <summary>Strongly-typed lookup. Returns null on miss or wrong type.</summary>
    public static T? Get<T>(long id) where T : class
        => Get(id) as T;

    /// <summary>Release an id; idempotent.</summary>
    public static void Release(long id)
    {
        if (_table.TryRemove(id, out var h) && h.IsAllocated)
            h.Free();
    }
}
