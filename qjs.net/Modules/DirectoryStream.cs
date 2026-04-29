using QuickJsNet.Core;
using QuickJsNet.Interop;

namespace QuickJsNet.Modules;

/// <summary>
/// Streaming directory enumeration. <c>read(maxEntries)</c> returns the next
/// batch of entries (or <c>null</c> when the directory is exhausted). Tracked
/// by the engine so the underlying enumerator is disposed even when JS code
/// forgets to call <c>close()</c>.
/// </summary>
[JSExport]
public sealed partial class DirectoryStream : IDisposable
{
    private IEnumerator<string>? _enumerator;
    private readonly string _basePath;

    public string Path => _basePath;
    public bool Closed => _enumerator is null;

    internal DirectoryStream(string path)
    {
        _basePath = path;
        _enumerator = System.IO.Directory.EnumerateFileSystemEntries(path).GetEnumerator();
    }

    /// <summary>
    /// Read up to <paramref name="maxEntries"/> entries (default 64). Returns
    /// <c>null</c> when no more entries remain. Each entry is a plain object
    /// with <c>name</c>, <c>isFile</c>, <c>isDirectory</c>, and <c>size</c>.
    /// </summary>
    public Task<Dictionary<string, object>[]?> Read(int maxEntries = 64)
    {
        var en = _enumerator;
        if (en is null) throw new InvalidOperationException("DirectoryStream is closed");
        if (maxEntries <= 0) maxEntries = 64;
        return Task.Run<Dictionary<string, object>[]?>(() =>
        {
            var batch = new List<Dictionary<string, object>>(maxEntries);
            while (batch.Count < maxEntries && en.MoveNext())
            {
                var full = en.Current;
                var name = System.IO.Path.GetFileName(full);
                bool isDir = false;
                bool isFile = false;
                long size = -1;
                try
                {
                    var attr = System.IO.File.GetAttributes(full);
                    isDir = (attr & System.IO.FileAttributes.Directory) != 0;
                    isFile = !isDir;
                    if (isFile)
                        size = new System.IO.FileInfo(full).Length;
                }
                catch { /* keep defaults */ }
                batch.Add(new Dictionary<string, object>
                {
                    ["name"] = name,
                    ["isFile"] = isFile,
                    ["isDirectory"] = isDir,
                    ["size"] = size,
                });
            }
            return batch.Count == 0 ? null : batch.ToArray();
        });
    }

    public Task Close()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        var en = Interlocked.Exchange(ref _enumerator, null);
        if (en is null) return;
        try { en.Dispose(); } catch { /* swallow */ }
        GC.SuppressFinalize(this);
    }

    ~DirectoryStream() => Dispose();
}
