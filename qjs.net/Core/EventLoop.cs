using QuickJSNet.Bindings;
using System.Collections.Concurrent;

namespace QuickJsNet.Core;

/// <summary>
/// Provides an event loop for the QuickJS engine, enabling asynchronous
/// execution via background callbacks and timers (setTimeout / setInterval).
/// All JS-facing work is drained on the caller's thread to satisfy QuickJS's
/// single-threaded requirement.
/// </summary>
internal class EventLoop
{
    private readonly QuickJSRuntime _engine;
    private readonly ConcurrentQueue<Action> _callbackQueue = new ConcurrentQueue<Action>();
    private readonly Dictionary<int, TimerEntry> _timers = new Dictionary<int, TimerEntry>();
    private int _pendingAsyncOps;
    private int _nextTimerId = 1;

    private class TimerEntry
    {
        public long FireAtTicks;
        public JSValue Callback;
        public int IntervalMs;
        public bool Cancelled;
    }

    public EventLoop(QuickJSRuntime engine)
    {
        _engine = engine;
        InstallTimers();
    }

    /// <summary>
    /// Whether there is any pending work (async ops, queued callbacks, or timers).
    /// </summary>
    public bool HasPendingWork =>
        _pendingAsyncOps > 0 || !_callbackQueue.IsEmpty || _timers.Count > 0;

    /// <summary>
    /// Increment the in-flight async operation counter.
    /// Call BEFORE starting an async operation.
    /// </summary>
    internal void TrackAsyncOp()
    {
        Interlocked.Increment(ref _pendingAsyncOps);
    }

    /// <summary>
    /// Post a callback to be executed on the JS thread.
    /// Also decrements the pending async op counter (pairs with TrackAsyncOp).
    /// </summary>
    internal void Post(Action callback)
    {
        _callbackQueue.Enqueue(callback);
        Interlocked.Decrement(ref _pendingAsyncOps);
    }

    /// <summary>
    /// Process all pending timers, callbacks, and microtasks in one pass.
    /// Must be called on the JS thread.
    /// </summary>
    internal void DrainQueue()
    {
        ProcessTimers();

        while (_callbackQueue.TryDequeue(out var callback))
        {
            callback();
        }

        _engine.ExecutePendingJobs();
    }

    /// <summary>
    /// Block the current thread, pumping the event loop until all pending
    /// async operations, timers, and callbacks have completed.
    /// Must be called on the JS thread.
    /// </summary>
    internal void RunUntilDone()
    {
        var spinWait = new SpinWait();
        while (HasPendingWork)
        {
            DrainQueue();
            if (HasPendingWork)
                spinWait.SpinOnce();
        }
        // Final drain to process any last microtasks
        DrainQueue();
    }

    /// <summary>
    /// Install setTimeout, setInterval, clearTimeout, clearInterval,
    /// and queueMicrotask into the JS global scope.
    /// </summary>
    private void InstallTimers()
    {
        // setTimeout(callback, delayMs?): number
        _engine.RegisterGlobalFunction("setTimeout", args =>
        {
            if (args.Length < 1) return 0;
            var callback = _engine.DupValue(args[0]);
            int delay = args.Length > 1 ? _engine.GetInt32(args[1]) : 0;
            if (delay < 0) delay = 0;
            int id = _nextTimerId++;
            _timers[id] = new TimerEntry
            {
                FireAtTicks = DateTime.UtcNow.AddMilliseconds(delay).Ticks,
                Callback = callback,
                IntervalMs = 0,
                Cancelled = false
            };
            return id;
        }, 2);

        // setInterval(callback, intervalMs): number
        _engine.RegisterGlobalFunction("setInterval", args =>
        {
            if (args.Length < 2) return 0;
            var callback = _engine.DupValue(args[0]);
            int interval = _engine.GetInt32(args[1]);
            if (interval < 1) interval = 1;
            int id = _nextTimerId++;
            _timers[id] = new TimerEntry
            {
                FireAtTicks = DateTime.UtcNow.AddMilliseconds(interval).Ticks,
                Callback = callback,
                IntervalMs = interval,
                Cancelled = false
            };
            return id;
        }, 2);

        // clearTimeout(id): void
        _engine.RegisterGlobalFunction("clearTimeout", args =>
        {
            if (args.Length < 1) return null;
            int id = _engine.GetInt32(args[0]);
            CancelTimer(id);
            return null;
        }, 1);

        // clearInterval(id): void
        _engine.RegisterGlobalFunction("clearInterval", args =>
        {
            if (args.Length < 1) return null;
            int id = _engine.GetInt32(args[0]);
            CancelTimer(id);
            return null;
        }, 1);

        // queueMicrotask polyfill via Promise
        _engine.Eval(@"
if (typeof globalThis.queueMicrotask === 'undefined') {
    globalThis.queueMicrotask = function(fn) {
        Promise.resolve().then(fn);
    };
}
", "<eventloop-init>");
    }

    private void CancelTimer(int id)
    {
        if (_timers.ContainsKey(id))
        {
            var entry = _timers[id];
            if (!entry.Cancelled)
            {
                entry.Cancelled = true;
                _engine.FreeValue(entry.Callback);
            }
            _timers.Remove(id);
        }
    }

    private void ProcessTimers()
    {
        if (_timers.Count == 0) return;

        var now = DateTime.UtcNow.Ticks;
        var toFire = new List<int>();

        foreach (var kv in _timers)
        {
            if (!kv.Value.Cancelled && kv.Value.FireAtTicks <= now)
                toFire.Add(kv.Key);
        }

        foreach (var id in toFire)
        {
            if (!_timers.ContainsKey(id)) continue;
            var entry = _timers[id];
            if (entry.Cancelled) continue;

            // Hold an extra reference so that clearInterval/clearTimeout
            // called from inside the callback cannot free the function
            // while QJS_Call is still on the stack.
            var cb = _engine.DupValue(entry.Callback);

            var undef = QuickJSNative.QJS_NewUndefined();
            var result = _engine.Call(cb, undef);
            _engine.FreeValue(result);
            _engine.FreeValue(cb);

            // The callback may have cancelled this timer via clearInterval,
            // so re-check before rescheduling or cleaning up.
            if (!_timers.ContainsKey(id)) continue;

            if (entry.IntervalMs > 0 && !entry.Cancelled)
            {
                // Reschedule cumulatively (drift-free): next deadline = previous deadline + interval,
                // not now + interval — otherwise pump latency at each fire accumulates and the
                // timer drifts slower than wall-clock (e.g. 100ms interval pumped on a 16ms grain
                // ends up ~108ms per cycle = 8% slow). Cumulative scheduling matches browser /
                // Node.js behaviour and stays in phase with wall time.
                long intervalTicks = entry.IntervalMs * TimeSpan.TicksPerMillisecond;
                long next = entry.FireAtTicks + intervalTicks;
                long now2 = DateTime.UtcNow.Ticks;
                // If we're more than one full interval behind (e.g. tab was suspended / window
                // dragged / GC pause), skip missed fires instead of bursting — the spec allows
                // implementations to coalesce. Snap forward to (now + interval).
                if (next < now2 - intervalTicks)
                    next = now2 + intervalTicks;
                entry.FireAtTicks = next;
            }
            else if (!entry.Cancelled)
            {
                // One-shot: free callback and remove
                _engine.FreeValue(entry.Callback);
                _timers.Remove(id);
            }
        }
    }

    /// <summary>
    /// Evaluate JavaScript code with top-level await support.
    /// Uses our event loop to drive promise settlement instead of QJS_StdAwait,
    /// avoiding deadlocks when async operations post to the EventLoop queue.
    /// </summary>
    internal object? Execute(string code, string filename = "<eval>")
    {
        int flags = QuickJSNative.JS_EVAL_TYPE_GLOBAL | QuickJSNative.JS_EVAL_FLAG_ASYNC;
        var promise = _engine.EvalRaw(code, filename, flags);

        if (promise.IsException)
        {
            string error = GetExceptionString();
            throw new QuickJSException(error);
        }

        return RunUntilPromiseSettled(promise);
    }

    /// <summary>
    /// Invoke a global JavaScript function by name with arguments, supporting async functions.
    /// </summary>
    /// <param name="functionName"></param>
    /// <param name="args"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    internal object? Invoke(string functionName, params object[] args)
    {
        if (!_engine.TryGetFunction(functionName, out var func))
            throw new Exception($"Function '{functionName}' is undefined.");

        var jsArgs = new JSValue[args.Length];
        for (int i = 0; i < args.Length; i++)
            jsArgs[i] = _engine.ManagedToJSValue(args[i]);

        var global = QuickJSNative.QJS_GetGlobalObject(_engine.Context);
        var result = _engine.Call(func, global, jsArgs);

        for (int i = 0; i < jsArgs.Length; i++)
            _engine.FreeValue(jsArgs[i]);

        _engine.FreeValue(func);
        _engine.FreeValue(global);

        return RunUntilPromiseSettled(result);
    }

    /// <summary>
    /// Run the event loop until a promise settles (fulfilled or rejected).
    /// </summary>
    private object? RunUntilPromiseSettled(JSValue promise)
    {
        var ctx = _engine.Context;
        var spinWait = new SpinWait();
        try
        {
            while (true)
            {
                DrainQueue();

                int state = QuickJSNative.QJS_PromiseState(ctx, promise);
                if (state == -1)
                {
                    // 不是一个 Promise 对象，直接转换返回
                    var managed = _engine.JSValueToManaged(promise);
                    return _engine.JSValueToManaged(promise);
                }
                else if (state != 0)
                {
                    var result = QuickJSNative.QJS_PromiseResult(ctx, promise);


                    if (state == 2)
                    {
                        // 使用专门提取 Error 异常信息的逻辑解析 rejected promise
                        string errorMsg = GetExceptionStringFromValue(result);
                        QuickJSNative.QJS_FreeValue(ctx, result);
                        throw new QuickJSException(errorMsg ?? "Promise rejected");
                    }

                    if (result.IsObject)
                    {
                        result = QuickJSNative.QJS_GetPropertyStr(ctx, result, "value");
                    }

                    var managed = _engine.JSValueToManaged(result);
                    QuickJSNative.QJS_FreeValue(ctx, result);

                    return managed;
                }

                spinWait.SpinOnce();
            }
        }
        finally
        {
            QuickJSNative.QJS_FreeValue(ctx, promise);
        }
    }

    /// <summary>
    /// Extract the current exception string from the QuickJS context.
    /// </summary>
    private string GetExceptionString()
    {
        var ctx = _engine.Context;
        var ex = QuickJSNative.QJS_GetException(ctx);
        string msg = GetExceptionStringFromValue(ex);
        QuickJSNative.QJS_FreeValue(ctx, ex);
        return msg;
    }

    /// <summary>
    /// Evaluate Exception or Error properties properly across Promise and try/catch behaviors
    /// </summary>
    private string GetExceptionStringFromValue(JSValue ex)
    {
        var ctx = _engine.Context;
        string msg = _engine.GetString(ex) ?? "Unknown error";

        if (QuickJSNative.QJS_IsError(ctx, ex) != 0)
        {
            var stack = QuickJSNative.QJS_GetPropertyStr(ctx, ex, "stack");
            if (stack.IsString)
            {
                msg += "\n" + _engine.GetString(stack);
            }
            QuickJSNative.QJS_FreeValue(ctx, stack);
        }

        return msg;
    }

    /// <summary>
    /// Free all remaining timer callbacks. Call during cleanup.
    /// </summary>
    public void Dispose()
    {
        foreach (var kv in _timers)
        {
            if (!kv.Value.Cancelled)
                _engine.FreeValue(kv.Value.Callback);
        }
        _timers.Clear();
    }
}