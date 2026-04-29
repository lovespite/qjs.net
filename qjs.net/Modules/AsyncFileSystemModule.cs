using System.Text;
using QuickJsNet.Interop;

namespace QuickJsNet.Modules;

/// <summary>
/// Native [JSExport] proxy for asynchronous file system operations.
/// Each method returns a Task that the source generator marshals to a JS Promise.
/// Installed by the engine as <c>globalThis.fsAsync</c>.
/// </summary>
[JSExport]
public sealed partial class AsyncFileSystemModule
{
    private readonly string? _basePath;

    public AsyncFileSystemModule(string? basePath = null)
    {
        _basePath = basePath;
    }

    public Task<string?> ReadFile(string path, string? encoding = null) => Task.Run(() =>
    {
        var resolved = ResolvePath(path);
        if (!File.Exists(resolved))
            throw new FileNotFoundException("File not found: " + path);
        return (string?)File.ReadAllText(resolved, FileSystemModule.GetEncoding(encoding ?? "utf-8"));
    });

    public Task<byte[]?> ReadFileBytes(string path) => Task.Run(() =>
    {
        var resolved = ResolvePath(path);
        if (!File.Exists(resolved))
            throw new FileNotFoundException("File not found: " + path);
        return (byte[]?)File.ReadAllBytes(resolved);
    });

    public Task<bool> WriteFile(string path, string content, string? encoding = null) => Task.Run(() =>
    {
        var resolved = ResolvePath(path);
        EnsureParent(resolved);
        File.WriteAllText(resolved, content, FileSystemModule.GetEncoding(encoding ?? "utf-8"));
        return true;
    });

    public Task<bool> WriteFileBytes(string path, byte[] data) => Task.Run(() =>
    {
        var resolved = ResolvePath(path);
        EnsureParent(resolved);
        File.WriteAllBytes(resolved, data ?? Array.Empty<byte>());
        return true;
    });

    public Task<bool> AppendFile(string path, string content, string? encoding = null) => Task.Run(() =>
    {
        var resolved = ResolvePath(path);
        File.AppendAllText(resolved, content, FileSystemModule.GetEncoding(encoding ?? "utf-8"));
        return true;
    });

    /// <summary>Async-flavored counterpart to <see cref="FileSystemModule.OpenRead(string)"/>.</summary>
    public Task<Stream> OpenRead(string path) => Task.Run(() =>
    {
        var resolved = ResolvePath(path);
        var fs = new FileStream(resolved, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, useAsync: true);
        return new Stream(fs, readable: true, writable: false, ownsInner: true);
    });

    public Task<Stream> OpenWrite(string path, bool append = false) => Task.Run(() =>
    {
        var resolved = ResolvePath(path);
        EnsureParent(resolved);
        var mode = append ? FileMode.Append : FileMode.Create;
        var fs = new FileStream(resolved, mode, FileAccess.Write, FileShare.None,
            bufferSize: 4096, useAsync: true);
        return new Stream(fs, readable: false, writable: true, ownsInner: true);
    });

    public Task<DirectoryStream> OpenDir(string path) => Task.Run(() =>
    {
        var resolved = ResolvePath(path);
        if (!Directory.Exists(resolved))
            throw new DirectoryNotFoundException("Directory not found: " + path);
        return new DirectoryStream(resolved);
    });

    public Task<bool> Exists(string path) => Task.Run(() =>
    {
        var resolved = ResolvePath(path);
        return File.Exists(resolved) || Directory.Exists(resolved);
    });

    public Task<bool> IsFile(string path) => Task.Run(() => File.Exists(ResolvePath(path)));

    public Task<bool> IsDirectory(string path) => Task.Run(() => Directory.Exists(ResolvePath(path)));

    public Task<bool> Remove(string path) => Task.Run(() =>
    {
        var resolved = ResolvePath(path);
        if (File.Exists(resolved)) { File.Delete(resolved); return true; }
        if (Directory.Exists(resolved)) { Directory.Delete(resolved, true); return true; }
        return false;
    });

    public Task<bool> Rename(string oldPath, string newPath) => Task.Run(() =>
    {
        var s = ResolvePath(oldPath);
        var d = ResolvePath(newPath);
        if (File.Exists(s)) { File.Move(s, d); return true; }
        if (Directory.Exists(s)) { Directory.Move(s, d); return true; }
        return false;
    });

    public Task<bool> Copy(string src, string dst) => Task.Run(() =>
    {
        var s = ResolvePath(src);
        var d = ResolvePath(dst);
        if (!File.Exists(s)) throw new FileNotFoundException("Source file not found: " + src);
        EnsureParent(d);
        File.Copy(s, d, overwrite: true);
        return true;
    });

    public Task<bool> Mkdir(string path) => Task.Run(() =>
    {
        Directory.CreateDirectory(ResolvePath(path));
        return true;
    });

    public Task<string[]> ReadDir(string path) => Task.Run(() =>
    {
        var resolved = ResolvePath(path);
        if (!Directory.Exists(resolved)) return Array.Empty<string>();
        var entries = Directory.GetFileSystemEntries(resolved);
        var names = new string[entries.Length];
        for (int i = 0; i < entries.Length; i++)
            names[i] = Path.GetFileName(entries[i]);
        return names;
    });

    public Task<Dictionary<string, object>?> Stat(string path) => Task.Run(() =>
    {
        var resolved = ResolvePath(path);
        if (File.Exists(resolved))
        {
            var info = new FileInfo(resolved);
            return (Dictionary<string, object>?)new Dictionary<string, object>
            {
                ["size"] = info.Length,
                ["isFile"] = true,
                ["isDirectory"] = false,
                ["created"] = info.CreationTimeUtc.ToString("O"),
                ["modified"] = info.LastWriteTimeUtc.ToString("O"),
                ["accessed"] = info.LastAccessTimeUtc.ToString("O"),
            };
        }
        if (Directory.Exists(resolved))
        {
            var info = new DirectoryInfo(resolved);
            return (Dictionary<string, object>?)new Dictionary<string, object>
            {
                ["size"] = 0L,
                ["isFile"] = false,
                ["isDirectory"] = true,
                ["created"] = info.CreationTimeUtc.ToString("O"),
                ["modified"] = info.LastWriteTimeUtc.ToString("O"),
                ["accessed"] = info.LastAccessTimeUtc.ToString("O"),
            };
        }
        return null;
    });

    public Task<string> Getcwd() => Task.FromResult(Directory.GetCurrentDirectory());

    public Task<string> TempDir() => Task.FromResult(Path.GetTempPath());

    private string ResolvePath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (_basePath != null)
        {
            var full = Path.GetFullPath(Path.Combine(_basePath, path));
            var baseNorm = Path.GetFullPath(_basePath);
            if (!full.StartsWith(baseNorm, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException($"Path '{path}' escapes the sandbox.");
            return full;
        }
        return Path.GetFullPath(path);
    }

    private static void EnsureParent(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}
