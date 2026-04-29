using QuickJsNet.Core;
using QuickJsNet.Interop;
using QuickJSNet.Bindings;
using System.Text;

namespace QuickJsNet.Modules;

/// <summary>
/// A unified byte-stream wrapper exposed to JavaScript as a [JSExport] proxy.
/// Wraps any <see cref="System.IO.Stream"/> and supports cooperative async
/// read/write plus a C#-side <c>pipe</c> implementation that avoids per-chunk
/// JS↔native round-trips.
///
/// Lifetime: the engine tracks every Stream through
/// <see cref="QuickJSRuntime.TrackDisposable(IDisposable)"/>; if JavaScript
/// forgets to call <c>close()</c>, the underlying handle is still released
/// when the engine itself is disposed.
/// </summary>
[JSExport]
public sealed partial class Stream : IDisposable
{
    private System.IO.Stream? _inner;
    private readonly bool _ownsInner;

    public bool Readable { get; }
    public bool Writable { get; }

    public bool Closed => _inner is null;

    public long Position
    {
        get
        {
            var s = _inner;
            if (s is null) return -1;
            try { return s.CanSeek ? s.Position : -1; } catch { return -1; }
        }
    }

    public long Length
    {
        get
        {
            var s = _inner;
            if (s is null) return -1;
            try { return s.CanSeek ? s.Length : -1; } catch { return -1; }
        }
    }

    internal Stream(System.IO.Stream inner, bool readable, bool writable, bool ownsInner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        Readable = readable && inner.CanRead;
        Writable = writable && inner.CanWrite;
        _ownsInner = ownsInner;
    }

    /// <summary>
    /// Create an in-memory readable+writable stream backed by a
    /// <see cref="System.IO.MemoryStream"/>. Useful as a sink for
    /// <c>pipe(dest)</c> when collecting bytes for further processing.
    /// </summary>
    public static Stream Memory(int initialCapacity = 0)
    {
        var ms = initialCapacity > 0
            ? new System.IO.MemoryStream(initialCapacity)
            : new System.IO.MemoryStream();
        return new Stream(ms, readable: true, writable: true, ownsInner: true);
    }

    /// <summary>
    /// Read up to <paramref name="length"/> bytes into <paramref name="buf"/>
    /// starting at <paramref name="offset"/>. Returns the actual number of
    /// bytes read; 0 indicates end-of-stream.
    /// </summary>
    public Task<int> Read(Buffer buf, int offset, int length)
    {
        var inner = _inner ?? throw new InvalidOperationException("Stream is closed");
        if (!Readable) throw new InvalidOperationException("Stream is not readable");
        if (buf is null) throw new ArgumentNullException(nameof(buf));
        if (length <= 0) length = buf.Length - offset;
        if (offset < 0 || length < 0 || offset + length > buf.Length)
            throw new ArgumentOutOfRangeException(nameof(offset), "offset/length out of range");
        if (length == 0) return Task.FromResult(0);
        var mem = buf.AsMemory().Slice(offset, length);
        return inner.ReadAsync(mem).AsTask();
    }

    /// <summary>
    /// Write bytes from a Buffer / ArrayBuffer / string. Returns the number of
    /// bytes written. Strings are encoded as UTF-8.
    /// </summary>
    public Task<int> Write(JSValue data, int offset, int length, QuickJSRuntime runtime)
    {
        var inner = _inner ?? throw new InvalidOperationException("Stream is closed");
        if (!Writable) throw new InvalidOperationException("Stream is not writable");
        var bytes = ExtractBytes(runtime.Context, data)
            ?? throw new ArgumentException("data must be Buffer, ArrayBuffer, Uint8Array or string", nameof(data));
        if (offset < 0) offset = 0;
        if (length < 0) length = bytes.Length - offset;
        if (offset > bytes.Length) offset = bytes.Length;
        if (offset + length > bytes.Length) length = bytes.Length - offset;
        if (length == 0) return Task.FromResult(0);
        var slice = new ReadOnlyMemory<byte>(bytes, offset, length);
        return WriteAndReturn(inner, slice);
    }

    private static byte[]? ExtractBytes(IntPtr ctx, JSValue v)
    {
        if (v.IsNullOrUndefined) return null;
        // Try Buffer unwrap first (installed by Buffer's static ctor).
        if (JSInteropRuntime.BufferUnwrapHook is { } hook)
        {
            var b = hook(ctx, v);
            if (b is not null) return b;
        }
        // ArrayBuffer / Uint8Array
        var bufPtr = QuickJSNative.QJS_GetArrayBuffer(ctx, out var sizePtr, v);
        if (bufPtr != IntPtr.Zero)
        {
            var size = (int)sizePtr.ToUInt32();
            var arr = new byte[size];
            if (size > 0) System.Runtime.InteropServices.Marshal.Copy(bufPtr, arr, 0, size);
            return arr;
        }
        // String fallback (UTF-8)
        var s = JSInteropRuntime.ReadString(ctx, v);
        return s is null ? null : Encoding.UTF8.GetBytes(s);
    }

    private static async Task<int> WriteAndReturn(System.IO.Stream s, ReadOnlyMemory<byte> mem)
    {
        await s.WriteAsync(mem).ConfigureAwait(false);
        return mem.Length;
    }

    /// <summary>Flush any buffered bytes to the underlying handle.</summary>
    public Task Flush()
    {
        var inner = _inner;
        if (inner is null) return Task.CompletedTask;
        return inner.FlushAsync();
    }

    /// <summary>
    /// Close the stream. Idempotent: subsequent calls are no-ops. After close,
    /// other methods throw <see cref="InvalidOperationException"/>.
    /// </summary>
    public Task Close()
    {
        Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Read everything remaining and return as a new Buffer. Cap with
    /// <paramref name="maxBytes"/> to avoid unbounded allocation.
    /// </summary>
    public async Task<Buffer> ReadAll(int maxBytes)
    {
        var inner = _inner ?? throw new InvalidOperationException("Stream is closed");
        if (!Readable) throw new InvalidOperationException("Stream is not readable");
        using var ms = new System.IO.MemoryStream();
        var temp = new byte[8192];
        long total = 0;
        bool unlimited = maxBytes <= 0;
        while (true)
        {
            int want = temp.Length;
            if (!unlimited)
            {
                long left = maxBytes - total;
                if (left <= 0) break;
                if (left < want) want = (int)left;
            }
            int n = await inner.ReadAsync(temp.AsMemory(0, want)).ConfigureAwait(false);
            if (n == 0) break;
            ms.Write(temp, 0, n);
            total += n;
        }
        return Buffer.WrapBytes(ms.ToArray());
    }

    /// <summary>
    /// Pump bytes from this stream into <paramref name="dest"/> in fixed-size
    /// chunks until EOF. The loop runs in C# (no per-chunk JS callback). The
    /// destination is always closed when the source drains. Returns total
    /// bytes piped.
    /// </summary>
    public async Task<long> Pipe(Stream dest, int chunkSize)
    {
        if (dest is null) throw new ArgumentNullException(nameof(dest));
        var src = _inner ?? throw new InvalidOperationException("Source stream is closed");
        var dst = dest._inner ?? throw new InvalidOperationException("Destination stream is closed");
        if (!Readable) throw new InvalidOperationException("Source is not readable");
        if (!dest.Writable) throw new InvalidOperationException("Destination is not writable");
        if (chunkSize <= 0) chunkSize = 65536;

        var buf = new byte[chunkSize];
        long total = 0;
        while (true)
        {
            int n = await src.ReadAsync(buf.AsMemory(0, chunkSize)).ConfigureAwait(false);
            if (n == 0) break;
            await dst.WriteAsync(buf.AsMemory(0, n)).ConfigureAwait(false);
            total += n;
        }
        await dst.FlushAsync().ConfigureAwait(false);
        dest.Dispose();
        return total;
    }

    public void Dispose()
    {
        var inner = Interlocked.Exchange(ref _inner, null);
        if (inner is null) return;
        try
        {
            if (_ownsInner) inner.Dispose();
        }
        catch { /* swallow: best-effort cleanup */ }
        GC.SuppressFinalize(this);
    }

    ~Stream() => Dispose();
}
