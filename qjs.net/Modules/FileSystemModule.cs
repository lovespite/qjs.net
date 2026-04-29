using System.Text;
using QuickJsNet.Interop;

namespace QuickJsNet.Modules;

/// <summary>
/// Native [JSExport] proxy for synchronous file system operations.
/// Installed by the engine as <c>globalThis.fs</c>.
/// </summary>
[JSExport]
public sealed partial class FileSystemModule
{
    private readonly string? _basePath;

    public FileSystemModule(string? basePath = null)
    {
        _basePath = basePath;
    }

    public string? ReadFile(string path, string? encoding = null)
    {
        var resolved = ResolvePath(path);
        if (!File.Exists(resolved)) return null;
        return File.ReadAllText(resolved, GetEncoding(encoding ?? "utf-8"));
    }

    public byte[]? ReadFileBytes(string path)
    {
        var resolved = ResolvePath(path);
        if (!File.Exists(resolved)) return null;
        return File.ReadAllBytes(resolved);
    }

    public bool WriteFile(string path, string content, string? encoding = null)
    {
        var resolved = ResolvePath(path);
        EnsureParent(resolved);
        File.WriteAllText(resolved, content, GetEncoding(encoding ?? "utf-8"));
        return true;
    }

    public bool WriteFileBytes(string path, byte[] data)
    {
        var resolved = ResolvePath(path);
        EnsureParent(resolved);
        File.WriteAllBytes(resolved, data ?? Array.Empty<byte>());
        return true;
    }

    public bool AppendFile(string path, string content, string? encoding = null)
    {
        var resolved = ResolvePath(path);
        File.AppendAllText(resolved, content, GetEncoding(encoding ?? "utf-8"));
        return true;
    }

    /// <summary>
    /// Open a file for streaming reads. The returned <see cref="Stream"/> is
    /// tracked by the engine so the underlying handle is released even if JS
    /// forgets to <c>close()</c> (engine disposal is the safety net).
    /// </summary>
    public Stream OpenRead(string path)
    {
        var resolved = ResolvePath(path);
        var fs = new FileStream(resolved, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, useAsync: true);
        return new Stream(fs, readable: true, writable: false, ownsInner: true);
    }

    /// <summary>
    /// Open a file for streaming writes. When <paramref name="append"/> is
    /// false (default) the file is truncated.
    /// </summary>
    public Stream OpenWrite(string path, bool append = false)
    {
        var resolved = ResolvePath(path);
        EnsureParent(resolved);
        var mode = append ? FileMode.Append : FileMode.Create;
        var fs = new FileStream(resolved, mode, FileAccess.Write, FileShare.None,
            bufferSize: 4096, useAsync: true);
        return new Stream(fs, readable: false, writable: true, ownsInner: true);
    }

    /// <summary>
    /// Open a directory for streaming enumeration. Use the returned
    /// <see cref="DirectoryStream"/> when a directory may contain a large
    /// number of entries.
    /// </summary>
    public DirectoryStream OpenDir(string path)
    {
        var resolved = ResolvePath(path);
        if (!Directory.Exists(resolved))
            throw new DirectoryNotFoundException($"Directory not found: {path}");
        return new DirectoryStream(resolved);
    }

    public bool Exists(string path)
    {
        var resolved = ResolvePath(path);
        return File.Exists(resolved) || Directory.Exists(resolved);
    }

    public bool IsFile(string path) => File.Exists(ResolvePath(path));

    public bool IsDirectory(string path) => Directory.Exists(ResolvePath(path));

    public bool Remove(string path)
    {
        var resolved = ResolvePath(path);
        if (File.Exists(resolved)) { File.Delete(resolved); return true; }
        if (Directory.Exists(resolved)) { Directory.Delete(resolved, true); return true; }
        return false;
    }

    public bool Rename(string oldPath, string newPath)
    {
        var src = ResolvePath(oldPath);
        var dst = ResolvePath(newPath);
        if (File.Exists(src)) { File.Move(src, dst); return true; }
        if (Directory.Exists(src)) { Directory.Move(src, dst); return true; }
        return false;
    }

    public bool Copy(string src, string dst)
    {
        var s = ResolvePath(src);
        var d = ResolvePath(dst);
        if (!File.Exists(s)) return false;
        EnsureParent(d);
        File.Copy(s, d, overwrite: true);
        return true;
    }

    public bool Mkdir(string path)
    {
        Directory.CreateDirectory(ResolvePath(path));
        return true;
    }

    public string[] ReadDir(string path)
    {
        var resolved = ResolvePath(path);
        if (!Directory.Exists(resolved)) return Array.Empty<string>();
        var entries = Directory.GetFileSystemEntries(resolved);
        var names = new string[entries.Length];
        for (int i = 0; i < entries.Length; i++)
            names[i] = Path.GetFileName(entries[i]);
        return names;
    }

    public Dictionary<string, object>? Stat(string path)
    {
        var resolved = ResolvePath(path);
        if (File.Exists(resolved))
        {
            var info = new FileInfo(resolved);
            return new Dictionary<string, object>
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
            return new Dictionary<string, object>
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
    }

    public string Getcwd() => Directory.GetCurrentDirectory();

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

    internal static Encoding GetEncoding(string name) => name.ToLowerInvariant() switch
    {
        "utf-8" or "utf8" => Encoding.UTF8,
        "ascii" => Encoding.ASCII,
        "unicode" or "utf-16" or "utf16" => Encoding.Unicode,
        "utf-32" or "utf32" => Encoding.UTF32,
        "latin1" => Encoding.Latin1,
        _ => Encoding.UTF8,
    };
}
