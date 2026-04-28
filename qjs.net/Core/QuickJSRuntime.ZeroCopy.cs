using QuickJsNet.Utils;
using QuickJSNet.Bindings;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace QuickJsNet.Core;

/// <summary>
/// Zero / low-allocation surface for crossing the .NET ↔ QuickJS boundary.
/// <para>
/// All members in this partial complement the legacy copy-based APIs in
/// <see cref="QuickJSRuntime"/>; existing public methods are preserved for
/// binary compatibility.
/// </para>
/// </summary>
public partial class QuickJSRuntime
{
    // ────────────────────────────────────────────────────────────────────
    //  Delegates for borrowed-buffer / borrowed-span callbacks
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Callback receiving a borrowed view over a JS string's UTF-8 bytes.
    /// The span is valid only for the duration of the call.
    /// </summary>
    public delegate TResult JsStringSpanFunc<TResult>(ReadOnlySpan<byte> utf8);

    /// <summary>
    /// Callback receiving a borrowed view over an ArrayBuffer's bytes.
    /// The span is valid only for the duration of the call.
    /// </summary>
    public delegate TResult ArrayBufferSpanFunc<TResult>(ReadOnlySpan<byte> data);

    /// <summary>
    /// Span-based JS C-function handler signature. <paramref name="args"/> is
    /// a borrowed view directly over the native argv array — no allocation.
    /// </summary>
    public delegate JSValue JSCFunctionSpan(IntPtr ctx, JSValue thisVal, ReadOnlySpan<JSValue> args);

    // ────────────────────────────────────────────────────────────────────
    //  Strings — borrowed span over JS string bytes (no managed alloc)
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Borrow the UTF-8 bytes of a JS string and pass them as a
    /// <see cref="ReadOnlySpan{T}"/> to <paramref name="action"/>. The span
    /// lifetime is bounded by the callback; do not let it escape.
    /// </summary>
    public unsafe TResult WithJSString<TResult>(JSValue v, JsStringSpanFunc<TResult> action)
    {
        IntPtr cstr = QuickJSNative.QJS_ToCStringLen(_context, out var len, v);
        if (cstr == IntPtr.Zero)
            return action(ReadOnlySpan<byte>.Empty);
        try
        {
            int n = (int)len.ToUInt32();
            return action(new ReadOnlySpan<byte>((void*)cstr, n));
        }
        finally { QuickJSNative.QJS_FreeCString(_context, cstr); }
    }

    // ────────────────────────────────────────────────────────────────────
    //  ArrayBuffer — borrowed span over native bytes (no managed alloc)
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Borrow the bytes of a JS ArrayBuffer and pass them as a
    /// <see cref="ReadOnlySpan{T}"/> to <paramref name="action"/>. The span
    /// lifetime is bounded by the callback; the underlying memory is owned by
    /// QuickJS and must not be retained beyond the call.
    /// </summary>
    public unsafe TResult WithArrayBuffer<TResult>(JSValue v, ArrayBufferSpanFunc<TResult> action)
    {
        IntPtr ptr = QuickJSNative.QJS_GetArrayBuffer(_context, out var psize, v);
        if (ptr == IntPtr.Zero)
            return action(ReadOnlySpan<byte>.Empty);
        int size = checked((int)psize.ToUInt64());
        return action(new ReadOnlySpan<byte>((void*)ptr, size));
    }

    // ────────────────────────────────────────────────────────────────────
    //  ArrayBuffer — zero-copy creation (wrap pinned managed / native memory)
    // ────────────────────────────────────────────────────────────────────

    // The native callback reads opaque as a GCHandle.ToIntPtr(...) referencing
    // a PinHolder that owns the pin and (optionally) a MemoryHandle.
    private sealed class PinHolder
    {
        public GCHandle Pin;          // Pinned GCHandle for byte[] OR Normal handle for MemoryHandle owner
        public MemoryHandle MemHandle;
        public bool HasMemHandle;
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static unsafe void FreePinnedBufferCallback(IntPtr rt, IntPtr opaque, IntPtr ptr)
    {
        if (opaque == IntPtr.Zero) return;
        var holderHandle = GCHandle.FromIntPtr(opaque);
        if (holderHandle.Target is PinHolder h)
        {
            if (h.HasMemHandle) h.MemHandle.Dispose();
            if (h.Pin.IsAllocated) h.Pin.Free();
        }
        holderHandle.Free();
    }

    private static IntPtr GetFreePinnedCallbackPtr()
    {
        unsafe
        {
            return (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, IntPtr, IntPtr, void>)&FreePinnedBufferCallback;
        }
    }

    /// <summary>
    /// Wrap a managed <c>byte[]</c> as a JS ArrayBuffer without copying.
    /// The array is pinned for the lifetime of the ArrayBuffer; the pin is
    /// released automatically when QuickJS GC-collects the ArrayBuffer
    /// (or when <see cref="QuickJSNative.QJS_DetachArrayBuffer"/> is called).
    /// </summary>
    public unsafe JSValue NewArrayBufferNoCopy(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0)
            return QuickJSNative.QJS_NewArrayBufferCopy(_context, IntPtr.Zero, 0);

        var holder = new PinHolder { Pin = GCHandle.Alloc(data, GCHandleType.Pinned) };
        var holderHandle = GCHandle.Alloc(holder, GCHandleType.Normal);
        IntPtr buf = holder.Pin.AddrOfPinnedObject();
        return QuickJSNative.QJS_NewArrayBuffer(
            _context, buf, (nuint)data.Length,
            GetFreePinnedCallbackPtr(),
            GCHandle.ToIntPtr(holderHandle),
            isShared: 0);
    }

    /// <summary>
    /// Wrap a <see cref="Memory{T}"/> as a JS ArrayBuffer without copying.
    /// The memory is pinned (via <see cref="MemoryHandle"/>) for the lifetime
    /// of the ArrayBuffer.
    /// </summary>
    public unsafe JSValue NewArrayBufferNoCopy(Memory<byte> data)
    {
        if (data.Length == 0)
            return QuickJSNative.QJS_NewArrayBufferCopy(_context, IntPtr.Zero, 0);

        var memHandle = data.Pin();
        var holder = new PinHolder { MemHandle = memHandle, HasMemHandle = true };
        var holderHandle = GCHandle.Alloc(holder, GCHandleType.Normal);
        return QuickJSNative.QJS_NewArrayBuffer(
            _context, (IntPtr)memHandle.Pointer, (nuint)data.Length,
            GetFreePinnedCallbackPtr(),
            GCHandle.ToIntPtr(holderHandle),
            isShared: 0);
    }

    /// <summary>
    /// Wrap an externally-managed native memory range as a JS ArrayBuffer
    /// without copying. The caller is solely responsible for ensuring the
    /// memory remains valid until the ArrayBuffer is no longer referenced
    /// from JS — no free callback is registered.
    /// </summary>
    public JSValue NewArrayBufferUnowned(IntPtr ptr, nuint length)
    {
        if (length == 0)
            return QuickJSNative.QJS_NewArrayBufferCopy(_context, IntPtr.Zero, 0);
        return QuickJSNative.QJS_NewArrayBuffer(
            _context, ptr, length,
            freeFunc: IntPtr.Zero,
            opaque: IntPtr.Zero,
            isShared: 0);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Function calling — stackalloc argv (no heap alloc up to 16 args)
    // ────────────────────────────────────────────────────────────────────

    private const int StackArgvThreshold = 16;

    /// <summary>
    /// Call a JS function. For ≤16 arguments uses <c>stackalloc</c>; for
    /// larger argument lists rents from <see cref="ArrayPool{T}"/>.
    /// </summary>
    internal unsafe JSValue CallFast(JSValue func, JSValue thisObj, ReadOnlySpan<JSValue> args)
    {
        if (args.Length == 0)
            return QuickJSNative.QJS_Call(_context, func, thisObj, 0, IntPtr.Zero);

        if (args.Length <= StackArgvThreshold)
        {
            Span<JSValue> buf = stackalloc JSValue[StackArgvThreshold];
            args.CopyTo(buf);
            fixed (JSValue* p = buf)
                return QuickJSNative.QJS_Call(_context, func, thisObj, args.Length, (IntPtr)p);
        }

        var rented = ArrayPool<JSValue>.Shared.Rent(args.Length);
        try
        {
            args.CopyTo(rented);
            fixed (JSValue* p = rented)
                return QuickJSNative.QJS_Call(_context, func, thisObj, args.Length, (IntPtr)p);
        }
        finally { ArrayPool<JSValue>.Shared.Return(rented); }
    }

    // ────────────────────────────────────────────────────────────────────
    //  Function registration — span-based handler (zero argv copy)
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Register a JS C-function whose handler receives the argv as a
    /// <see cref="ReadOnlySpan{T}"/> directly over the native buffer — no
    /// per-call array allocation. Recommended for new code.
    /// </summary>
    internal void RegisterFunctionSpan(JSValue obj, string name,
        Func<IntPtr, JSValue, ReadOnlySpan<JSValue>, JSValue> handler, int argCount = 0)
    {
        QuickJSNative.JSCFunction nativeFunc = (IntPtr ctx, JSValue thisVal, int argc, IntPtr argv) =>
        {
            try
            {
                unsafe
                {
                    var span = argc == 0
                        ? ReadOnlySpan<JSValue>.Empty
                        : new ReadOnlySpan<JSValue>((void*)argv, argc);
                    return handler(ctx, thisVal, span);
                }
            }
            catch (Exception ex)
            {
                return ThrowInternalError(ctx, ex.Message);
            }
        };

        var gcHandle = GCHandle.Alloc(nativeFunc);
        _pinnedDelegates.Add(gcHandle);
        var funcPtr = Marshal.GetFunctionPointerForDelegate(nativeFunc);

        Utf8StringHelper.WithUtf8(name, (pName, _) =>
        {
            QuickJSNative.QJS_SetPropertyFunctionStrPtr(_context, obj, pName, funcPtr, argCount);
            return 0;
        });
    }

    /// <summary>
    /// Throw a JS internal error from a string — pointer-based, no marshaller alloc.
    /// </summary>
    internal static JSValue ThrowInternalError(IntPtr ctx, string message)
    {
        return Utf8StringHelper.WithUtf8(message, (p, _) =>
            QuickJSNative.QJS_ThrowInternalErrorPtr(ctx, p));
    }

    // ────────────────────────────────────────────────────────────────────
    //  Convenience: read/write strings without managed-string allocation in
    //  pass-through scenarios (e.g. JS string → stream).
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Copy the UTF-8 bytes of a JS string into a managed <c>byte[]</c>.
    /// This is a single allocation (the array) instead of two
    /// (<c>byte[]</c> + <see cref="string"/>) compared to
    /// <c>Encoding.UTF8.GetBytes(GetString(v))</c>.
    /// </summary>
    public byte[] GetStringUtf8(JSValue v)
    {
        return WithJSString(v, span => span.ToArray());
    }

    /// <summary>
    /// Convenience: convert a JS string to a managed <see cref="string"/>
    /// using the borrowed-span path (single transcoding pass, one allocation).
    /// </summary>
    public string GetStringFast(JSValue v)
    {
        return WithJSString(v, static span => span.IsEmpty ? string.Empty : Encoding.UTF8.GetString(span));
    }
}
