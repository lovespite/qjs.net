using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using QuickJSNet.Bindings;
using QuickJsNet.Core;

namespace QuickJsNet.Interop;

/// <summary>
/// Helper for <c>on(name, fn)</c> / <c>off(name, fn)</c> JS bindings to C#
/// events. Tracks <c>(jsCallback, handler)</c> pairs per instance so that an
/// <c>off</c> call can find and detach the matching delegate without reflection.
/// </summary>
public static class JSEventBridge
{
    public sealed class Subscription
    {
        public Delegate Handler { get; init; } = null!;
        public JSValue JSCallback;
        public QuickJSRuntime Runtime { get; init; } = null!;
    }

    private static readonly ConditionalWeakTable<object, ConcurrentDictionary<string, List<Subscription>>> _byInstance = new();

    public static Subscription CreateSubscription(QuickJSRuntime rt, JSValue jsFn, Delegate handler)
    {
        var dup = QuickJSNative.QJS_DupValue(rt.Context, jsFn);
        return new Subscription { Handler = handler, JSCallback = dup, Runtime = rt };
    }

    public static void Invoke(Subscription sub, JSValue[] args)
    {
        var ctx = sub.Runtime.Context;
        var thisV = QuickJSNative.QJS_NewUndefined();
        unsafe
        {
            fixed (JSValue* p = args)
            {
                var ret = QuickJSNative.QJS_Call(ctx, sub.JSCallback, thisV, args.Length, (IntPtr)p);
                QuickJSNative.QJS_FreeValue(ctx, ret);
            }
        }
        for (int i = 0; i < args.Length; i++) QuickJSNative.QJS_FreeValue(ctx, args[i]);
    }

    public static void Track(object instance, string eventName, Subscription sub)
    {
        var map = _byInstance.GetOrCreateValue(instance);
        var list = map.GetOrAdd(eventName, _ => new List<Subscription>());
        lock (list) list.Add(sub);
    }

    public static Subscription? UntrackByJSValue(object instance, string eventName, JSValue jsFn)
    {
        if (!_byInstance.TryGetValue(instance, out var map)) return null;
        if (!map.TryGetValue(eventName, out var list)) return null;
        lock (list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                if (s.JSCallback.Tag == jsFn.Tag && s.JSCallback.Ptr == jsFn.Ptr)
                {
                    list.RemoveAt(i);
                    QuickJSNative.QJS_FreeValue(s.Runtime.Context, s.JSCallback);
                    return s;
                }
            }
        }
        return null;
    }
}
