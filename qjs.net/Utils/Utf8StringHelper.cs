using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace QuickJsNet.Utils;

/// <summary>
/// Helpers for passing managed <see cref="string"/> values to native code as
/// NUL-terminated UTF-8 buffers without going through the .NET P/Invoke
/// marshaller (which always allocates a fresh native buffer per call).
/// <para>
/// Strategy:
/// <list type="bullet">
/// <item>
/// Small strings (encoded UTF-8 ≤ <see cref="StackThreshold"/> bytes including
/// the trailing NUL) use a caller-supplied <c>stackalloc</c> buffer — zero
/// heap allocation.
/// </item>
/// <item>
/// Larger strings rent a buffer from <see cref="ArrayPool{T}.Shared"/> — at
/// most one pooled allocation, returned immediately after the call.
/// </item>
/// </list>
/// </para>
/// </summary>
public static class Utf8StringHelper
{
    /// <summary>
    /// Maximum encoded UTF-8 length (including trailing NUL) that can fit in
    /// a typical <c>stackalloc</c> buffer reserved by callers. Strings longer
    /// than this fall back to the <see cref="ArrayPool{T}.Shared"/>.
    /// </summary>
    public const int StackThreshold = 256;

    /// <summary>
    /// Compute the maximum number of bytes (including trailing NUL) required
    /// to encode <paramref name="s"/> as UTF-8. Conservative upper bound; never
    /// allocates.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetMaxUtf8ByteCount(string? s)
        => s is null ? 1 : Encoding.UTF8.GetMaxByteCount(s.Length) + 1;

    /// <summary>
    /// Delegate accepting a borrowed UTF-8 buffer. The pointer is valid only
    /// for the duration of the callback.
    /// </summary>
    public unsafe delegate TResult Utf8Func<TResult>(IntPtr utf8Ptr, nuint length);

    /// <summary>
    /// Delegate accepting two borrowed UTF-8 buffers (e.g. <c>QJS_Eval(input, filename)</c>).
    /// </summary>
    public unsafe delegate TResult Utf8Func2<TResult>(
        IntPtr utf8Ptr1, nuint length1,
        IntPtr utf8Ptr2, nuint length2);

    /// <summary>
    /// Encode <paramref name="value"/> into a NUL-terminated UTF-8 buffer
    /// (stack or pooled), invoke <paramref name="action"/>, and return its
    /// result. The pointer is valid only inside the callback.
    /// <para>
    /// <paramref name="length"/> passed to the callback is the encoded byte
    /// count <em>not</em> including the trailing NUL — matching the
    /// <c>size_t len</c> argument expected by the QuickJS <c>*_Len</c> APIs.
    /// </para>
    /// </summary>
    public static unsafe TResult WithUtf8<TResult>(string? value, Utf8Func<TResult> action)
    {
        if (value is null)
            return action(IntPtr.Zero, 0);
        if (value.Length == 0)
        {
            byte zero = 0;
            return action((IntPtr)(&zero), 0);
        }

        int max = Encoding.UTF8.GetMaxByteCount(value.Length) + 1;
        if (max <= StackThreshold)
        {
            Span<byte> stack = stackalloc byte[StackThreshold];
            int written = Encoding.UTF8.GetBytes(value, stack);
            stack[written] = 0;
            fixed (byte* p = stack)
                return action((IntPtr)p, (nuint)written);
        }

        byte[] rented = ArrayPool<byte>.Shared.Rent(max);
        try
        {
            int written = Encoding.UTF8.GetBytes(value, rented);
            rented[written] = 0;
            fixed (byte* p = rented)
                return action((IntPtr)p, (nuint)written);
        }
        finally { ArrayPool<byte>.Shared.Return(rented); }
    }

    /// <summary>
    /// Encode two strings into UTF-8 buffers and invoke the callback with both.
    /// Used by APIs that need <c>(input, filename)</c> together.
    /// </summary>
    public static unsafe TResult WithUtf8<TResult>(string? a, string? b, Utf8Func2<TResult> action)
    {
        return WithUtf8(a, (pa, la) =>
            WithUtf8(b, (pb, lb) => action(pa, la, pb, lb)));
    }
}
