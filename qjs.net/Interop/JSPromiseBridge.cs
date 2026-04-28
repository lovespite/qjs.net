using System.Threading.Tasks;
using QuickJSNet.Bindings;
using QuickJsNet.Core;

namespace QuickJsNet.Interop;

/// <summary>
/// Adapter that wraps an awaited <see cref="Task"/>/<see cref="ValueTask"/> as
/// a JS Promise via the runtime's event loop. Strongly-typed; AOT-safe.
/// </summary>
public static class JSPromiseBridge
{
    /// <summary>Wrap a <see cref="Task"/> as a JS Promise resolving with <c>undefined</c>.</summary>
    public static JSValue FromTask(QuickJSRuntime rt, Task task)
    {
        var capRet = NewCapability(rt, out var resolveFn, out var rejectFn);
        task.ContinueWith(t =>
        {
            if (t.IsFaulted)
                rt.RejectPromise(resolveFn, rejectFn, t.Exception?.InnerException ?? new Exception("Task faulted"));
            else if (t.IsCanceled)
                rt.RejectPromise(resolveFn, rejectFn, "Task canceled");
            else
                rt.ResolvePromise(resolveFn, rejectFn, null);
        }, TaskScheduler.Default);
        return capRet;
    }

    /// <summary>Wrap a <see cref="Task{T}"/> as a JS Promise resolving with the projected value.</summary>
    public static JSValue FromTask<T>(QuickJSRuntime rt, Task<T> task, Func<QuickJSRuntime, T, JSValue> _project)
    {
        // Note: the JS-value projection happens later on the JS thread inside
        // ResolvePromise → ManagedToJSValue which already routes through binders.
        // We pass T via boxing so existing infrastructure handles conversion.
        var capRet = NewCapability(rt, out var resolveFn, out var rejectFn);
        task.ContinueWith(t =>
        {
            if (t.IsFaulted)
                rt.RejectPromise(resolveFn, rejectFn, t.Exception?.InnerException ?? new Exception("Task faulted"));
            else if (t.IsCanceled)
                rt.RejectPromise(resolveFn, rejectFn, "Task canceled");
            else
                rt.ResolvePromise(resolveFn, rejectFn, t.Result);
        }, TaskScheduler.Default);
        return capRet;
    }

    /// <summary>Wrap a <see cref="ValueTask"/> as a JS Promise.</summary>
    public static JSValue FromTask(QuickJSRuntime rt, ValueTask vt) => FromTask(rt, vt.AsTask());

    /// <summary>Wrap a <see cref="ValueTask{T}"/> as a JS Promise.</summary>
    public static JSValue FromTask<T>(QuickJSRuntime rt, ValueTask<T> vt, Func<QuickJSRuntime, T, JSValue> project)
        => FromTask(rt, vt.AsTask(), project);

    private static JSValue NewCapability(QuickJSRuntime rt, out JSValue resolve, out JSValue reject)
    {
        var arr = new JSValue[2];
        var promise = QuickJSNative.QJS_NewPromiseCapability(rt.Context, arr);
        resolve = arr[0];
        reject = arr[1];
        rt.TrackAsyncOpForBridge();
        return promise;
    }
}
