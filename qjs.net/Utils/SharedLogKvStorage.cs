using Microsoft.Win32.SafeHandles;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;

namespace QuickJsNet.Utils;


/// <summary>
/// 支持多进程并发读写的日志型键值存储。
/// <para>采用 Actor 模型（单线程任务队列）串行化 IO 操作。</para>
/// <para>引入 CRC32 校验保证数据完整性。</para>
/// <para>内存仅存储文件指针（索引），大幅降低内存占用。</para>
/// </summary>
public class SharedLogKvStorage : IDisposable
{
    public const int MAX_RECORD_PAYLOAD_SIZE = 100 * 1024 * 1024; // 100 MB 

    private readonly FileStream _dfs;
    private readonly FileStream _lfs;
    private readonly FileLock _fLock;
    private FileStream DataFileStream => _dfs;

    // 修改：内存索引只存储文件指针，不再存储完整的 Value 字符串
    private readonly ConcurrentDictionary<string, ValuePointer> _memoryIndex;

    // --- 任务队列相关 ---
    private readonly Channel<Action> _taskQueue;
    private readonly Task _workerTask;
    private readonly CancellationTokenSource _cts;
    private bool _disposed;

    private long _lastSyncedPosition = 0;

    private const byte OP_NOP = 0;
    private const byte OP_SET = 1;
    private const byte OP_DEL = 2;

    // 数据头长度：Length(4) + CRC(4) = 8
    private const int HEADER_SIZE = 8;

    public long FileSize => DataFileStream?.Length ?? 0;
    public bool IsDisposed => _disposed;

    public string Name => Path.GetFileNameWithoutExtension(_dfs.Name);

    /// <summary>
    /// 指向文件内部 Value 位置的指针结构
    /// </summary>
    private readonly struct ValuePointer
    {
        public ValuePointer(long offset) => Offset = offset;

        public readonly long Offset; // Value 在文件中的起始偏移量（包含长度前缀）
        public static readonly ValuePointer Null = new ValuePointer(-1);
    }

    private SharedLogKvStorage(FileStream dfs, FileStream lfs)
    {
        //_dataFilePath = dfs.Name; 
        //_lockFilePath = lfs.Name;
        _dfs = dfs; _lfs = lfs;
        _lastSyncedPosition = 0;
        _fLock = new FileLock(_lfs.SafeFileHandle, Name);

        // 初始化内存索引
        _memoryIndex = new ConcurrentDictionary<string, ValuePointer>();

        // 初始化任务队列
        _taskQueue = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        // 启动处理任务队列的后台任务
        _cts = new CancellationTokenSource();
        _workerTask = Task.Factory.StartNew(
            ProcessQueueLoop,
            TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach
        );
    }

    public static SharedLogKvStorage Open(string dataFilePath)
    {
        var dir = Path.GetDirectoryName(dataFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        var lockFilePath = Path.ChangeExtension(dataFilePath, ".lock");
        FileStream? lfs = null, dfs = null;

        // 初始化文件流
        try
        {
            lfs = new FileStream(lockFilePath
                               , FileMode.OpenOrCreate
                               , FileAccess.ReadWrite
                               , FileShare.ReadWrite
                               , bufferSize: 1 // No buffering needed for lock file
                               );
            dfs = new FileStream(dataFilePath
                               , FileMode.OpenOrCreate
                               , FileAccess.ReadWrite
                               , FileShare.ReadWrite
                               , bufferSize: 4096
                               );
        }
        catch
        {
            lfs?.Dispose();
            dfs?.Dispose();
            throw;
        }

        var storage = new SharedLogKvStorage(dfs, lfs);

        storage.SyncDataInternal();

        return storage;
    }

    private async Task ProcessQueueLoop()
    {
        var reader = _taskQueue.Reader;
        try
        {
            while (await reader.WaitToReadAsync(_cts.Token))
            {
                while (reader.TryRead(out var action))
                {
                    try { action(); }
                    catch (Exception ex) { LogError($"Critical error in file loop: {ex}"); }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { LogError($"Actor loop crash: {ex}"); }
    }

    private Task<T> EnqueueOperationAsync<T>(Func<T> operation)
    {
        // ObjectDisposedException.ThrowIf(_disposed, this);
        if (_disposed) throw new ObjectDisposedException(nameof(SharedLogKvStorage), "Cannot enqueue operation on a disposed storage.");
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_taskQueue.Writer.TryWrite(() =>
        {
            try { tcs.SetResult(operation()); }
            catch (Exception ex) { tcs.SetException(ex); }
        }))
        {
            tcs.SetException(new InvalidOperationException("Storage queue is closed."));
        }
        return tcs.Task;
    }

    private Task EnqueueOperationAsync(Action operation)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SharedLogKvStorage), "Cannot enqueue operation on a disposed storage.");
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_taskQueue.Writer.TryWrite(() =>
        {
            try { operation(); tcs.SetResult(true); }
            catch (Exception ex) { tcs.SetException(ex); }
        }))
        {
            tcs.SetException(new InvalidOperationException("Storage queue is closed."));
        }
        return tcs.Task;
    }

    #region Lock

    //private void EnterWriteLock() => _fLock.LockExclusive(); 

    //private void ExitWriteLock() => _fLock.Unlock();

    #endregion

    #region Internal File Operations


    private void SyncDataInternal()
    {
        long currentFileLength = DataFileStream.Length;
        if (currentFileLength == _lastSyncedPosition) return;
        if (currentFileLength < _lastSyncedPosition)
        {
            // 文件被截断，必须重建索引
            // 通常可能是因为被其他实例压缩或重写
            _memoryIndex.Clear();
            _lastSyncedPosition = 0;
        }

        DataFileStream.Seek(_lastSyncedPosition, SeekOrigin.Begin);

        using var reader = new BinaryReader(DataFileStream, Encoding.UTF8, leaveOpen: true);
        while (DataFileStream.Position < currentFileLength)
        {
            long recordStartPos = DataFileStream.Position;

            try
            {
                int ret;
                // 读取并校验，获取 Payload 数据块
                if ((ret = ReadRecordPayload(reader, out var payload)) != RRP_OK)
                {
                    LogError($"RRP error at {recordStartPos}: {ret}");
                    // 读取失败，可能是文件被截断，停止同步
                    break;
                }

                // 解析 Payload 来更新索引
                // Payload 结构: [Op 1B] + [Key (VarInt+Bytes)] + [Value (VarInt+Bytes)]?

                // 计算 Payload 在文件中的绝对起始位置
                // 记录开始位置 + Header(8 bytes)
                long payloadFileStartOffset = recordStartPos + HEADER_SIZE;

                var (OpCode, Key, Pointer) = ParsePayloadAndIndex(payload, payloadFileStartOffset);

                switch (OpCode)
                {
                    case OP_DEL:
                        _memoryIndex.TryRemove(Key, out _);
                        break;
                    case OP_SET:
                        _memoryIndex[Key] = Pointer;
                        break;
                    default:
                        break;
                }

                _lastSyncedPosition = DataFileStream.Position;
            }
            catch (Exception ex)
            {
                LogError($"Sync error at {recordStartPos}: {ex.Message}");
                break;
            }
    ;

        }
    }


    #endregion

    #region Static Helpers

    /// <summary>
    /// 解析 Payload 
    /// </summary>
    /// <param name="payload"></param>
    /// <param name="payloadFileStartOffset"></param>
    /// <returns></returns>
    private static (byte OpCode, string Key, ValuePointer Pointer) ParsePayloadAndIndex(byte[] payload, long payloadFileStartOffset)
    {
        using var ms = new MemoryStream(payload);
        using var reader = new BinaryReader(ms, Encoding.UTF8);

        byte op = reader.ReadByte();
        string key = reader.ReadString();

        if (op == OP_SET)
        {
            // BinaryReader.ReadString() 读取了 Key
            // 现在 Stream 的位置就是 Value 的起始位置（包含长度前缀）
            // 我们需要计算出这个位置相对于文件的绝对偏移量
            long valueOffsetInPayload = ms.Position;
            long absoluteValueOffset = payloadFileStartOffset + valueOffsetInPayload;

            // _memoryIndex[key] = new ValuePointer(absoluteValueOffset);
            return (op, key, new ValuePointer(absoluteValueOffset));
        }

        // _memoryIndex.TryRemove(key, out _);
        return (op, key, ValuePointer.Null);
    }

    /// <summary>
    /// 写入记录并返回该记录 Payload 中 Value 的绝对文件偏移量
    /// </summary>
    private static ValuePointer? WriteRecord(Stream stream, byte op, string key, string? value)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms, Encoding.UTF8);
        writer.Write(op);
        writer.Write(key);

        long valueOffsetInPayload = -1;
        if (op == OP_SET && value != null)
        {
            valueOffsetInPayload = ms.Position; // 记录 Value 在 Payload 中的相对位置
            writer.Write(value);
        }

        byte[] payload = ms.ToArray();

        // 写入物理文件
        long recordStartPos = stream.Position;
        using (var diskWriter = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            diskWriter.Write(payload.Length);
            diskWriter.Write(Crc32.Compute(payload));
            diskWriter.Write(payload);
        }

        if (valueOffsetInPayload != -1)
        {
            // Header Size = 4 (Length) + 4 (CRC) = 8
            long absoluteValueOffset = recordStartPos + HEADER_SIZE + valueOffsetInPayload;
            return new ValuePointer(absoluteValueOffset);
        }

        return null;
    }

    public const int RRP_OK = 0;
    public const int RRP_E_INVALID_LENGTH = -1;
    public const int RRP_E_CRC_MISMATCH = -2;
    public const int RRP_EOF = -3;
    public const int RRP_E_PAYLOAD_LENGTH_MISMATCH = -4;
    public const int RRP_E_UNKNOWN = -99;

    /// <summary>
    /// 读取记录并校验 CRC，返回原始 Payload 数据
    /// </summary>
    private static int ReadRecordPayload(BinaryReader reader, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        int length;
        try { length = reader.ReadInt32(); } catch (EndOfStreamException) { return RRP_EOF; }

        if (length <= 0 || length > MAX_RECORD_PAYLOAD_SIZE)
        {
            return RRP_E_INVALID_LENGTH;
        }

        uint storedCrc;
        try
        {
            storedCrc = reader.ReadUInt32();
            payload = reader.ReadBytes(length);
        }
        catch (EndOfStreamException) { return RRP_EOF; }

        if (payload.Length != length) return RRP_E_PAYLOAD_LENGTH_MISMATCH;

        var actualCrc = Crc32.Compute(payload);
        if (actualCrc != storedCrc)
        {
            return RRP_E_CRC_MISMATCH;
        }

        return RRP_OK;
    }

    /// <summary>
    /// 从文件指定位置读取 Value 字符串
    /// </summary>
    private static string? ReadValueFromFile(Stream dataStream, ValuePointer pointer)
    {
        // 保存当前位置
        long originalPos = dataStream.Position;
        try
        {
            dataStream.Seek(pointer.Offset, SeekOrigin.Begin);
            using var reader = new BinaryReader(dataStream, Encoding.UTF8, leaveOpen: true);
            return reader.ReadString();
        }
        catch (Exception ex)
        {
            LogError($"Failed to read value at {pointer.Offset}: {ex.Message}");
            return null;
        }
        finally
        {
            // 恢复位置（虽然在 Actor 模型单线程中通常不是必须的，因为每次操作都会 Seek，但为了保险起见）
            dataStream.Seek(originalPos, SeekOrigin.Begin);
        }
    }

    private static void LogError(string message)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [SharedKv] {message}");
    }

    #endregion

    #region Public APIs

    public Task SetAsync(string key, string json)
    {
        if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
        return EnqueueOperationAsync(() =>
        {
            using var scopeLock = _fLock.AcquireScope();

            SyncDataInternal();
            if (DataFileStream is null) throw new InvalidOperationException("Stream closed");

            DataFileStream.Seek(0, SeekOrigin.End);

            // 写入并获取新的指针
            var ptr = WriteRecord(DataFileStream, OP_SET, key, json);
            DataFileStream.Flush();

            if (ptr.HasValue)
            {
                _memoryIndex[key] = ptr.Value;
            }

            _lastSyncedPosition = DataFileStream.Position;

        });
    }

    public Task SetBatchAsync(ICollection<KeyValuePair<string, string>> data)
    {
        return EnqueueOperationAsync(() =>
        {
            using var scopeLock = _fLock.AcquireScope();
            SyncDataInternal();
            if (DataFileStream is null) throw new InvalidOperationException("Stream closed");

            DataFileStream.Seek(0, SeekOrigin.End);
            foreach (var kvp in data)
            {
                var ptr = WriteRecord(DataFileStream, OP_SET, kvp.Key, kvp.Value);
                if (ptr.HasValue)
                {
                    _memoryIndex[kvp.Key] = ptr.Value;
                }
            }
            DataFileStream.Flush();
            _lastSyncedPosition = DataFileStream.Position;
        });
    }

    public Task RemoveAsync(string key)
    {
        return EnqueueOperationAsync(() =>
        {
            if (!_memoryIndex.ContainsKey(key)) return;

            using var scopeLock = _fLock.AcquireScope();
            SyncDataInternal();
            if (DataFileStream is null) throw new InvalidOperationException("Stream closed");

            DataFileStream.Seek(0, SeekOrigin.End);
            WriteRecord(DataFileStream, OP_DEL, key, null);
            DataFileStream.Flush();

            _memoryIndex.TryRemove(key, out _);
            _lastSyncedPosition = DataFileStream.Position;

        });
    }

    public Task RemoveBatchAsync(ICollection<string> keys)
    {
        return EnqueueOperationAsync(() =>
        {
            using var scopeLock = _fLock.AcquireScope();
            SyncDataInternal();
            if (DataFileStream is null) throw new InvalidOperationException("Stream closed");

            DataFileStream.Seek(0, SeekOrigin.End);
            foreach (var key in keys)
            {
                WriteRecord(DataFileStream, OP_DEL, key, null);
                _memoryIndex.TryRemove(key, out _);
            }
            DataFileStream.Flush();

            _lastSyncedPosition = DataFileStream.Position;
        });
    }

    public Task ClearAsync()
    {
        return EnqueueOperationAsync(() =>
        {
            using var scopeLock = _fLock.AcquireScope();
            if (DataFileStream is null) throw new InvalidOperationException("Stream closed");
            DataFileStream.SetLength(0);
            DataFileStream.Flush();
            _memoryIndex.Clear();
            _lastSyncedPosition = 0;

        });
    }

    public Task<string?> GetAsync(string key)
    {
        return EnqueueOperationAsync(() =>
        {
            using var scopeLock = _fLock.AcquireScope();
            SyncDataInternal();

            if (_memoryIndex.TryGetValue(key, out var ptr))
            {
                // 现在需要从文件读取
                return ReadValueFromFile(DataFileStream, ptr);
            }
            return null;
        });
    }

    public Task<ICollection<KeyValuePair<string, string>>> GetBatchAsync(ICollection<string> keys)
    {
        return EnqueueOperationAsync<ICollection<KeyValuePair<string, string>>>(() =>
        {
            using var scopeLock = _fLock.AcquireScope();
            SyncDataInternal();

            var list = new List<KeyValuePair<string, string>>();
            foreach (var k in keys)
            {
                if (_memoryIndex.TryGetValue(k, out var ptr))
                {
                    var val = ReadValueFromFile(DataFileStream, ptr);
                    if (val != null)
                    {
                        list.Add(new KeyValuePair<string, string>(k, val));
                    }
                }
            }
            return list;
        });
    }

    public Task<bool> HasKeyAsync(string key)
    {
        return EnqueueOperationAsync(() =>
        {
            using var scopeLock = _fLock.AcquireScope();
            SyncDataInternal();

            return _memoryIndex.ContainsKey(key);
        });
    }

    public Task<ICollection<string>> GetKeysAsync()
    {
        return EnqueueOperationAsync<ICollection<string>>(() =>
        {
            using var scopeLock = _fLock.AcquireScope();
            return _memoryIndex.Keys.ToArray();
        });
    }

    public async Task<ICollection<string>> FindKeysAsync(string keyword)
    {
        return await EnqueueOperationAsync(() =>
        {
            using var scopeLock = _fLock.AcquireScope();
            return _memoryIndex.Keys.Where(k => k.Contains(keyword)).ToList();
        });
    }

    public async Task Compact()
    {
        await EnqueueOperationAsync(async () =>
        {
            try
            {
                using var scopeLock = _fLock.AcquireScope();

                if (DataFileStream is null) throw new InvalidOperationException("Stream closed");

                SyncDataInternal();

                var tempPath = Path.GetTempFileName();
                if (File.Exists(tempPath)) File.Delete(tempPath);

                using var tempFs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, bufferSize: 4096, FileOptions.DeleteOnClose);
                // 遍历索引，从旧文件中读取出实际数据，写入到新文件
                CopyToInternal(tempFs);
                await tempFs.FlushAsync();

                tempFs.Seek(0, SeekOrigin.Begin);
                DataFileStream.Seek(0, SeekOrigin.Begin);
                DataFileStream.SetLength(0); // 清空旧文件

                await tempFs.CopyToAsync(DataFileStream);
                _lastSyncedPosition = 0; // 重置同步位置
                SyncDataInternal();
            }
            catch (Exception ex)
            {
                LogError($"Compact failed: {ex}");
            }
        });
    }

    public async Task<(long Total, long Valid)> CheckFileValidityAsync(ICollection<(string msg, long ptr, int err)> errorList)
    {
        return await EnqueueOperationAsync(() =>
        {
            using var scopeLock = _fLock.AcquireScope();
            errorList.Clear();
            var originalPtr = DataFileStream.Position;
            long counter = 0;
            var keys = new HashSet<string>();

            try
            {
                DataFileStream.Seek(0, SeekOrigin.Begin);
                using var reader = new BinaryReader(DataFileStream, Encoding.UTF8, leaveOpen: true);
                long ptr = reader.BaseStream.Position;
                while (true)
                {
                    int ret; ptr = reader.BaseStream.Position;
                    // 读取并校验，获取 Payload 数据块
                    if ((ret = ReadRecordPayload(reader, out var payload)) != RRP_OK)
                    {
                        if (ret == RRP_EOF)
                        {
                            // 正常结束
                            break;
                        }
                        else
                        {
                            errorList.Add(("RRP-ERR", ptr, ret));
                        }
                    }
                    else
                    {
                        var (OpCode, Key, _) = ParsePayloadAndIndex(payload, payloadFileStartOffset: 0); // 我们只需要 Key，因此 Offset 传入 0 即可

                        if (OpCode == OP_SET)
                            keys.Add(Key);
                        else if (OpCode == OP_DEL)
                            keys.Remove(Key);

                        ++counter;
                    }
                }


            }
            catch (Exception ex)
            {
                errorList.Add(($"EXCEPTION: {ex.Message}", DataFileStream.Position, RRP_E_UNKNOWN));
            }
            finally
            {
                DataFileStream.Position = originalPtr;
            }
            return (counter, keys.Count);
        });
    }

    #endregion

    private void CopyToInternal(Stream tempFs)
    {
        foreach (var kvp in _memoryIndex)
        {
            var key = kvp.Key;
            var ptr = kvp.Value;
            var value = ReadValueFromFile(DataFileStream, ptr);

            if (value != null)
            {
                WriteRecord(tempFs, OP_SET, key, value);
            }
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _taskQueue.Writer.TryComplete();
                _cts.Cancel();
                try { _workerTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
                _cts.Dispose();
                _dfs.Dispose();
                _lfs.Dispose();
                try { File.Delete(_lfs.Name); } catch { }
            }
            _disposed = true;
            GC.Collect();
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~SharedLogKvStorage() => Dispose(false);
}


internal class FileLock : IDisposable
{
    private readonly SafeFileHandle _handle;

    // --- 配置字段 (Config) ---
    // 这些值用于下一次锁定尝试
    private long _configOffset = long.MaxValue - 1;
    private long _configSize = 1;

    // --- 状态字段 (State) ---
    // 这些值记录当前实际锁定的区域，仅在 _isLocked = true 时有效
    private bool _isLocked;
    private long _actualLockedOffset;
    private long _actualLockedSize;

    private readonly object @lock = new object();

    public readonly struct Scope : IDisposable
    {
        private readonly FileLock _fileLock;
        public Scope(FileLock fileLock)
        {
            _fileLock = fileLock;
            _fileLock.LockExclusive();
        }
        public void Dispose()
        {
            _fileLock.Unlock();
        }
    }

    public Scope AcquireScope() => new Scope(this);

    /// <summary>
    /// Gets or sets the starting byte offset of the lock.
    /// </summary>
    public long LockOffset
    {
        get => _configOffset;
        set
        {
            if (_isLocked) throw new InvalidOperationException("Cannot change LockOffset while the lock is held.");
            _configOffset = value;
        }
    }

    /// <summary>
    /// Gets or sets the length of the byte range to be locked.
    /// </summary>
    public long LockSize
    {
        get => _configSize;
        set
        {
            if (_isLocked) throw new InvalidOperationException("Cannot change LockSize while the lock is held.");
            _configSize = value;
        }
    }

    public FileLock AtRange(long start, long len)
    {
        LockOffset = start;
        LockSize = len;
        return this;
    }

    public bool IsLocked
    {
        get { lock (@lock) { return _isLocked; } }
    }

    public string Name { get; }

    public FileLock(SafeFileHandle handle, string name)
    {
        if (handle is null) throw new ArgumentNullException(nameof(handle));
        if (handle.IsInvalid || handle.IsClosed)
            throw new ArgumentException("Handle is invalid or closed.", nameof(handle));

        _handle = handle;
        Name = name;
    }

    public FileLock(SafeFileHandle handle) : this(handle, Guid.NewGuid().ToString("N")) { }

    #region Native API

    [DllImport("kernel32.dll", EntryPoint = "LockFileEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private extern static bool LockFileEx(IntPtr hFile, uint dwFlags, uint dwReserved, uint nNumberOfBytesToLockLow, uint nNumberOfBytesToLockHigh, ref OVERLAPPED lpOverlapped);

    [DllImport("kernel32.dll", EntryPoint = "UnlockFileEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private extern static bool UnlockFileEx(IntPtr hFile, uint dwReserved, uint nNumberOfBytesToUnlockLow, uint nNumberOfBytesToUnlockHigh, ref OVERLAPPED lpOverlapped);

    [StructLayout(LayoutKind.Sequential)]
    private struct OVERLAPPED
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public IntPtr hEvent;
    }

    private const uint LOCKFILE_EXCLUSIVE_LOCK = 0x00000002;
    private const uint LOCKFILE_FAIL_IMMEDIATELY = 0x00000001;

    private const int ERROR_NOT_LOCKED = 158;
    private const int ERROR_LOCK_VIOLATION = 33;

    #endregion

    public void LockExclusive(TimeSpan timeout)
    {
        if (_isLocked) throw new InvalidOperationException("FileLock is already acquired.");
        lock (@lock)
        {
            if (_isLocked) throw new InvalidOperationException("FileLock is already acquired.");
            LockInternal(timeout);
        }
    }

    private void LockInternal(TimeSpan timeout)
    {
        // ObjectDisposedException.ThrowIf(_handle.IsClosed || _handle.IsInvalid, _handle);
        if (_handle.IsClosed || _handle.IsInvalid) throw new ObjectDisposedException(nameof(FileLock), "Cannot acquire lock on a closed or invalid handle.");

        // 1. 获取配置快照
        long targetOffset = _configOffset;
        long targetSize = _configSize;

        //ArgumentOutOfRangeException.ThrowIfLessThan(targetOffset, 0, nameof(LockOffset));
        //ArgumentOutOfRangeException.ThrowIfLessThan(targetSize, 1, nameof(LockSize));

        if (targetOffset < 0) throw new ArgumentOutOfRangeException(nameof(LockOffset), "LockOffset must be non-negative.");
        if (targetSize < 0) targetSize = 0;

        // 准备 Overlapped
        var overlapped = new OVERLAPPED
        {
            Offset = (uint)(targetOffset & 0xFFFFFFFF),
            OffsetHigh = (uint)(targetOffset >> 32),
            hEvent = IntPtr.Zero
        };

        uint sizeLow = (uint)(targetSize & 0xFFFFFFFF);
        uint sizeHigh = (uint)(targetSize >> 32);

        // 安全引用 Handle
        bool refAdded = false;
        try
        {
            _handle.DangerousAddRef(ref refAdded);
            IntPtr rawHandle = _handle.DangerousGetHandle();

            // 等待循环
            // 使用 SpinWait 结构虽然高效，但对于文件IO锁，如果超时时间较长
            var spinWait = new SpinWait();
            long startTicks = Stopwatch.GetTimestamp();

            while (true)
            {
                var ok = LockFileInternal(timeout, targetOffset, targetSize, ref overlapped, sizeLow, sizeHigh, rawHandle, startTicks);
                if (ok) return;
                spinWait.SpinOnce();
                if (spinWait.NextSpinWillYield) Thread.Sleep(1);
            }
        }
        finally
        {
            if (refAdded) _handle.DangerousRelease();
        }
    }

    private bool LockFileInternal(TimeSpan timeout, long targetOffset, long targetSize, ref OVERLAPPED overlapped, uint sizeLow, uint sizeHigh, IntPtr rawHandle, long startTicks)
    {
        // 尝试立即锁定
        if (LockFileEx(rawHandle, LOCKFILE_EXCLUSIVE_LOCK | LOCKFILE_FAIL_IMMEDIATELY, 0, sizeLow, sizeHigh, ref overlapped))
        {
            // 成功：更新内部状态
            _isLocked = true;
            _actualLockedOffset = targetOffset;
            _actualLockedSize = targetSize;
            return true;
        }

        int error = Marshal.GetLastWin32Error();

        // 只有 "Lock Violation" (33) 才是需要重试的情况
        if (error != ERROR_LOCK_VIOLATION)
        {
            throw new System.ComponentModel.Win32Exception(error);
        }

        // 检查超时
        if (timeout != TimeSpan.Zero && GetElapsedTime(startTicks) > timeout)
        {
            throw new TimeoutException($"Failed to acquire file lock at offset {targetOffset} within {timeout.TotalMilliseconds}ms.");
        }

        // 立即失败模式
        if (timeout == TimeSpan.Zero)
        {
            throw new TimeoutException("Failed to acquire file lock immediately.");
        }

        return false;
    }

    private static TimeSpan GetElapsedTime(long startTicks)
    {
        long elapsedTicks = Stopwatch.GetTimestamp() - startTicks;
        return TimeSpan.FromTicks(elapsedTicks * TimeSpan.TicksPerSecond / Stopwatch.Frequency);
    }

    public void LockExclusive() => LockExclusive(TimeSpan.FromMilliseconds(int.MaxValue));

    public bool TryLockExclusiveImmediate()
    {
        try
        {
            LockExclusive(TimeSpan.Zero);
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
    }

    public void Unlock()
    {
        if (!_isLocked) return;
        lock (@lock)
        {
            if (!_isLocked) return;

            // 使用 _actualLockedOffset (锁定时的快照)，确保解锁区域与锁定区域一致 
            var overlapped = new OVERLAPPED
            {
                Offset = (uint)(_actualLockedOffset & 0xFFFFFFFF),
                OffsetHigh = (uint)(_actualLockedOffset >> 32),
                hEvent = IntPtr.Zero
            };
            uint sizeLow = (uint)(_actualLockedSize & 0xFFFFFFFF);
            uint sizeHigh = (uint)(_actualLockedSize >> 32);

            // 2. 解锁
            bool refAdded = false;
            try
            {
                _handle.DangerousAddRef(ref refAdded);
                IntPtr rawHandle = _handle.DangerousGetHandle();

                if (!UnlockFileEx(rawHandle, 0, sizeLow, sizeHigh, ref overlapped))
                {
                    int error = Marshal.GetLastWin32Error();
                    if (error != ERROR_NOT_LOCKED)
                    {
                        throw new System.ComponentModel.Win32Exception(error);
                    }
                }
            }
            finally
            {
                // 状态重置
                _isLocked = false;
                _actualLockedOffset = 0;
                _actualLockedSize = 0;

                if (refAdded) _handle.DangerousRelease();
            }
        }
    }

    public void Dispose()
    {
        Unlock();
        GC.SuppressFinalize(this);
    }
}


internal class Crc32
{
    private static readonly uint[] Table;
    static Crc32()
    {
        Table = new uint[256];
        const uint polynomial = 0xEDB88320;
        for (uint i = 0; i < Table.Length; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
            {
                if ((crc & 1) == 1)
                    crc = (crc >> 1) ^ polynomial;
                else
                    crc >>= 1;
            }
            Table[i] = crc;
        }
    }
    public static uint Compute(byte[] bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in bytes)
        {
            byte index = (byte)((crc & 0xFF) ^ b);
            crc = (crc >> 8) ^ Table[index];
        }
        return ~crc;
    }
}