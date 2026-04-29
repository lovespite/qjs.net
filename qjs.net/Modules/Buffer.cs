using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using QuickJsNet.Core;
using QuickJsNet.Interop;
using QuickJSNet.Bindings;

namespace QuickJsNet.Modules;

/// <summary>
/// Mutable in-memory byte buffer modeled after Node.js <c>Buffer</c>, exposed
/// to JavaScript as <c>globalThis.Buffer</c>. Backed by a <see cref="Memory{T}"/>
/// view over a shared owner array so <see cref="Slice"/> is allocation-free.
/// </summary>
/// <remarks>
/// Indexing via <c>buf[i]</c> is NOT supported (QuickJS exotic-class plumbing
/// would defeat the source-generated wrapper). Use <see cref="Get"/> /
/// <see cref="Set"/> instead, or call <see cref="ToUint8Array"/> for an
/// indexable JS view (which copies).
/// </remarks>
[JSExport]
public sealed partial class Buffer
{
    // Static constructor wires the global Interop hook so that anywhere a
    // managed `byte[]` parameter is expected (writeFileBytes, fetch body…)
    // we can auto-unwrap a Buffer instance.
    static Buffer()
    {
        JSInteropRuntime.BufferUnwrapHook = (ctx, v) =>
        {
            if (!v.IsObject) return null;
            var id = JSInteropRuntime.ReadId(ctx, v);
            if (id == 0) return null;
            var buf = JSObjectTable.Get<Buffer>(id);
            return buf?.ToArrayCopy();
        };
    }

    private Memory<byte> _mem;

    private Buffer(Memory<byte> mem) { _mem = mem; }

    internal static Buffer WrapBytes(byte[] data) => new Buffer(data ?? Array.Empty<byte>());

    /// <summary>Length in bytes.</summary>
    public int Length => _mem.Length;

    // ───────────────────── Static factories ─────────────────────

    /// <summary>Allocate a zero- or fill-initialised buffer of <paramref name="size"/> bytes.</summary>
    public static Buffer Alloc(int size, int fill = 0)
    {
        if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
        var arr = new byte[size];
        if (fill != 0) Array.Fill(arr, unchecked((byte)fill));
        return new Buffer(arr);
    }

    /// <summary>
    /// Build a Buffer from a string (encoded with <paramref name="encoding"/>),
    /// an <c>ArrayBuffer</c>, a Node-style byte array, or another <c>Buffer</c>.
    /// Default encoding is <c>"utf8"</c>; <c>"base64"</c> and <c>"hex"</c> are
    /// also accepted for string sources.
    /// </summary>
    public static Buffer From(JSValue source, string? encoding, QuickJSRuntime runtime)
    {
        var ctx = runtime.Context;
        if (source.IsString)
        {
            var s = JSInteropRuntime.ReadString(ctx, source) ?? "";
            return FromString(s, encoding ?? "utf8");
        }
        if (source.IsObject)
        {
            // Buffer instance?
            var id = JSInteropRuntime.ReadId(ctx, source);
            if (id != 0 && JSObjectTable.Get<Buffer>(id) is { } other)
            {
                var copy = new byte[other._mem.Length];
                other._mem.Span.CopyTo(copy);
                return new Buffer(copy);
            }
            // ArrayBuffer / Uint8Array
            var bufPtr = QuickJSNative.QJS_GetArrayBuffer(ctx, out var sizePtr, source);
            if (bufPtr != IntPtr.Zero)
            {
                int sz = (int)sizePtr.ToUInt32();
                var arr = new byte[sz];
                if (sz > 0) Marshal.Copy(bufPtr, arr, 0, sz);
                return new Buffer(arr);
            }
        }
        throw new ArgumentException("Buffer.from: unsupported source type");
    }

    private static Buffer FromString(string s, string encoding)
    {
        switch (encoding.ToLowerInvariant())
        {
            case "utf8":
            case "utf-8":
                return new Buffer(Encoding.UTF8.GetBytes(s));
            case "ascii":
                return new Buffer(Encoding.ASCII.GetBytes(s));
            case "latin1":
            case "binary":
                return new Buffer(Encoding.Latin1.GetBytes(s));
            case "utf16":
            case "utf-16":
            case "unicode":
                return new Buffer(Encoding.Unicode.GetBytes(s));
            case "base64":
                return new Buffer(Convert.FromBase64String(s));
            case "hex":
                return new Buffer(HexToBytes(s));
            default:
                throw new ArgumentException($"Unsupported encoding: {encoding}");
        }
    }

    /// <summary>
    /// Concatenate a list of buffers into a new buffer. <paramref name="totalLength"/>
    /// when negative is computed from inputs; when non-negative the result is
    /// truncated/padded to that exact size.
    /// </summary>
    public static Buffer Concat(JSValue list, int totalLength, QuickJSRuntime runtime)
    {
        var ctx = runtime.Context;
        if (list.IsNullOrUndefined)
            throw new ArgumentNullException(nameof(list));
        int len = JSInteropRuntime.ReadArrayLength(ctx, list);
        var items = new Buffer?[len];
        int needed = 0;
        for (int i = 0; i < len; i++)
        {
            var elv = JSInteropRuntime.ReadArrayItem(ctx, list, (uint)i);
            try
            {
                long id = JSInteropRuntime.ReadId(ctx, elv);
                if (id != 0 && JSObjectTable.Get<Buffer>(id) is { } b)
                {
                    items[i] = b;
                    needed += b._mem.Length;
                }
            }
            finally
            {
                JSInteropRuntime.Free(ctx, elv);
            }
        }
        int outLen = totalLength < 0 ? needed : totalLength;
        var result = new byte[outLen];
        int offset = 0;
        for (int i = 0; i < len && offset < outLen; i++)
        {
            var src = items[i];
            if (src is null) continue;
            int copy = Math.Min(src._mem.Length, outLen - offset);
            src._mem.Span.Slice(0, copy).CopyTo(result.AsSpan(offset, copy));
            offset += copy;
        }
        return new Buffer(result);
    }

    // ───────────────────── Indexed accessors ─────────────────────

    public int Get(int index)
    {
        if ((uint)index >= (uint)_mem.Length)
            throw new ArgumentOutOfRangeException(nameof(index), "index out of range");
        return _mem.Span[index];
    }

    public void Set(int index, int value)
    {
        if ((uint)index >= (uint)_mem.Length)
            throw new ArgumentOutOfRangeException(nameof(index), "index out of range");
        _mem.Span[index] = unchecked((byte)value);
    }

    // ───────────────────── Slicing ─────────────────────

    /// <summary>
    /// Return a new Buffer that shares the underlying memory in the half-open
    /// range <c>[start, end)</c>. Mutations through either view are visible to
    /// the other. Negative indices are not supported.
    /// </summary>
    public Buffer Slice(int start = 0, int end = -1)
    {
        int len = _mem.Length;
        if (end < 0 || end > len) end = len;
        if (start < 0) start = 0;
        if (start > end) start = end;
        return new Buffer(_mem.Slice(start, end - start));
    }

    public int Copy(Buffer target, int targetStart = 0, int sourceStart = 0, int sourceEnd = -1)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (sourceEnd < 0 || sourceEnd > _mem.Length) sourceEnd = _mem.Length;
        if (sourceStart < 0) sourceStart = 0;
        if (sourceStart > sourceEnd) sourceStart = sourceEnd;
        if (targetStart < 0) targetStart = 0;
        if (targetStart >= target._mem.Length) return 0;
        int copy = Math.Min(sourceEnd - sourceStart, target._mem.Length - targetStart);
        if (copy <= 0) return 0;
        _mem.Span.Slice(sourceStart, copy)
            .CopyTo(target._mem.Span.Slice(targetStart, copy));
        return copy;
    }

    public void Fill(int value, int start = 0, int end = -1)
    {
        if (end < 0 || end > _mem.Length) end = _mem.Length;
        if (start < 0) start = 0;
        if (start >= end) return;
        _mem.Span.Slice(start, end - start).Fill(unchecked((byte)value));
    }

    public bool Equals(Buffer other)
    {
        if (other is null) return false;
        return _mem.Span.SequenceEqual(other._mem.Span);
    }

    public int IndexOf(int value, int from = 0)
    {
        if (from < 0) from = 0;
        if (from >= _mem.Length) return -1;
        var span = _mem.Span;
        byte b = unchecked((byte)value);
        for (int i = from; i < span.Length; i++)
            if (span[i] == b) return i;
        return -1;
    }

    // ───────────────────── Encoding ─────────────────────

    /// <summary>
    /// Decode the buffer (or sub-range) as a string. Default encoding is
    /// <c>"utf8"</c>. Supported: <c>"utf8"/"utf-8"</c>, <c>"ascii"</c>,
    /// <c>"latin1"</c>, <c>"utf16"/"utf-16"/"unicode"</c>, <c>"base64"</c>,
    /// <c>"hex"</c> (lower-case, no separator).
    /// </summary>
    public string ToString(string? encoding)
    {
        var span = _mem.Span;
        switch ((encoding ?? "utf8").ToLowerInvariant())
        {
            case "utf8":
            case "utf-8":
                return Encoding.UTF8.GetString(span);
            case "ascii":
                return Encoding.ASCII.GetString(span);
            case "latin1":
            case "binary":
                return Encoding.Latin1.GetString(span);
            case "utf16":
            case "utf-16":
            case "unicode":
                return Encoding.Unicode.GetString(span);
            case "base64":
                return Convert.ToBase64String(span);
            case "hex":
                return BytesToHex(span);
            default:
                throw new ArgumentException($"Unsupported encoding: {encoding}");
        }
    }

    /// <summary>Copy the bytes into a new ArrayBuffer (Uint8Array view in JS).</summary>
    public byte[] ToUint8Array() => ToArrayCopy();

    /// <summary>Alias for <see cref="ToUint8Array"/>.</summary>
    public byte[] ToArrayBuffer() => ToArrayCopy();

    // ───────────────────── Numeric I/O ─────────────────────

    public int ReadUInt8(int offset) => _mem.Span[offset];
    public int ReadInt8(int offset) => unchecked((sbyte)_mem.Span[offset]);
    public int ReadUInt16LE(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(_mem.Span.Slice(offset, 2));
    public int ReadUInt16BE(int offset) => BinaryPrimitives.ReadUInt16BigEndian(_mem.Span.Slice(offset, 2));
    public int ReadInt16LE(int offset) => BinaryPrimitives.ReadInt16LittleEndian(_mem.Span.Slice(offset, 2));
    public int ReadInt16BE(int offset) => BinaryPrimitives.ReadInt16BigEndian(_mem.Span.Slice(offset, 2));
    public long ReadUInt32LE(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_mem.Span.Slice(offset, 4));
    public long ReadUInt32BE(int offset) => BinaryPrimitives.ReadUInt32BigEndian(_mem.Span.Slice(offset, 4));
    public int ReadInt32LE(int offset) => BinaryPrimitives.ReadInt32LittleEndian(_mem.Span.Slice(offset, 4));
    public int ReadInt32BE(int offset) => BinaryPrimitives.ReadInt32BigEndian(_mem.Span.Slice(offset, 4));
    public double ReadFloat32LE(int offset) => BinaryPrimitives.ReadSingleLittleEndian(_mem.Span.Slice(offset, 4));
    public double ReadFloat64LE(int offset) => BinaryPrimitives.ReadDoubleLittleEndian(_mem.Span.Slice(offset, 8));

    public void WriteUInt8(int value, int offset) => _mem.Span[offset] = unchecked((byte)value);
    public void WriteInt8(int value, int offset) => _mem.Span[offset] = unchecked((byte)(sbyte)value);
    public void WriteUInt16LE(int value, int offset) => BinaryPrimitives.WriteUInt16LittleEndian(_mem.Span.Slice(offset, 2), unchecked((ushort)value));
    public void WriteUInt16BE(int value, int offset) => BinaryPrimitives.WriteUInt16BigEndian(_mem.Span.Slice(offset, 2), unchecked((ushort)value));
    public void WriteInt16LE(int value, int offset) => BinaryPrimitives.WriteInt16LittleEndian(_mem.Span.Slice(offset, 2), unchecked((short)value));
    public void WriteInt16BE(int value, int offset) => BinaryPrimitives.WriteInt16BigEndian(_mem.Span.Slice(offset, 2), unchecked((short)value));
    public void WriteUInt32LE(long value, int offset) => BinaryPrimitives.WriteUInt32LittleEndian(_mem.Span.Slice(offset, 4), unchecked((uint)value));
    public void WriteUInt32BE(long value, int offset) => BinaryPrimitives.WriteUInt32BigEndian(_mem.Span.Slice(offset, 4), unchecked((uint)value));
    public void WriteInt32LE(int value, int offset) => BinaryPrimitives.WriteInt32LittleEndian(_mem.Span.Slice(offset, 4), value);
    public void WriteInt32BE(int value, int offset) => BinaryPrimitives.WriteInt32BigEndian(_mem.Span.Slice(offset, 4), value);
    public void WriteFloat32LE(double value, int offset) => BinaryPrimitives.WriteSingleLittleEndian(_mem.Span.Slice(offset, 4), (float)value);
    public void WriteFloat64LE(double value, int offset) => BinaryPrimitives.WriteDoubleLittleEndian(_mem.Span.Slice(offset, 8), value);

    // ───────────────────── Internal accessors (consumed by Stream) ─────────────────────

    /// <summary>Read-only span over the live memory. Caller must not retain.</summary>
    internal ReadOnlySpan<byte> AsSpan() => _mem.Span;

    /// <summary>Mutable span for writers (e.g. Stream.read fills this view).</summary>
    internal Span<byte> AsWritableSpan() => _mem.Span;

    internal Memory<byte> AsMemory() => _mem;

    /// <summary>Materialise an independent byte[] copy.</summary>
    internal byte[] ToArrayCopy()
    {
        var copy = new byte[_mem.Length];
        _mem.Span.CopyTo(copy);
        return copy;
    }

    // ───────────────────── Helpers ─────────────────────

    private static string BytesToHex(ReadOnlySpan<byte> bytes)
    {
        const string lookup = "0123456789abcdef";
        var sb = new StringBuilder(bytes.Length * 2);
        for (int i = 0; i < bytes.Length; i++)
        {
            byte b = bytes[i];
            sb.Append(lookup[b >> 4]);
            sb.Append(lookup[b & 0xF]);
        }
        return sb.ToString();
    }

    private static byte[] HexToBytes(string hex)
    {
        if (string.IsNullOrEmpty(hex)) return Array.Empty<byte>();
        if ((hex.Length & 1) != 0) throw new ArgumentException("Hex string must have an even length");
        var bytes = new byte[hex.Length / 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            int hi = HexNibble(hex[i * 2]);
            int lo = HexNibble(hex[i * 2 + 1]);
            bytes[i] = unchecked((byte)((hi << 4) | lo));
        }
        return bytes;
    }

    private static int HexNibble(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => throw new ArgumentException($"Invalid hex char: '{c}'"),
    };
}
