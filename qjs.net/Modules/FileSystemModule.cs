using QuickJsNet.Core;
using QuickJSNet.Bindings;
using System.Text;

namespace QuickJsNet.Modules;

/// <summary>
/// Registers file system functions into the QuickJS engine, accessible from JavaScript
/// via the __fs global object. Provides: readFile, writeFile, appendFile, exists,
/// remove, rename, mkdir, readDir, stat, copy.
/// </summary>
internal static class FileSystemModule
{
    /// <summary>
    /// Install file system functions on the JS engine.
    /// Available in JS as: __fs.readFile(path), __fs.writeFile(path, content), etc.
    /// An optional basePath can restrict all operations to a directory for sandboxing.
    /// </summary>
    public static void Install(QuickJSRuntime engine, string? basePath = null)
    {
        var ctx = engine.Context;
        var fsObj = QuickJSNative.QJS_NewObject(ctx);

        // __fs.readFile(path: string, encoding?: string): string | null
        engine.RegisterFunction(fsObj, "readFile", args =>
        {
            if (args.Length < 1) return null;
            string? path = engine.GetString(args[0]);
            if (path is null) return null;
            path = ResolvePath(path, basePath);
            if (!File.Exists(path)) return null;
            string encoding = args.Length > 1 ? (engine.GetString(args[1]) ?? "utf-8") : "utf-8";
            return File.ReadAllText(path, GetEncoding(encoding));
        }, 1);

        // __fs.readFileBytes(path: string): ArrayBuffer | null
        engine.RegisterFunction(fsObj, "readFileBytes", args =>
        {
            if (args.Length < 1) return null;
            string? path = engine.GetString(args[0]);
            if (path is null) return null;
            path = ResolvePath(path, basePath);
            if (!File.Exists(path)) return null;
            return File.ReadAllBytes(path);
        }, 1);

        // __fs.writeFile(path: string, content: string, encoding?: string): boolean
        engine.RegisterFunction(fsObj, "writeFile", args =>
        {
            if (args.Length < 2) return false;
            string? path = engine.GetString(args[0]);
            string? content = engine.GetString(args[1]);
            if (path is null || content is null) return false;
            path = ResolvePath(path, basePath);
            string encoding = args.Length > 2 ? (engine.GetString(args[2]) ?? "utf-8") : "utf-8";
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(path, content, GetEncoding(encoding));
            return true;
        }, 2);

        // __fs.writeFileBytes(path: string, buffer: ArrayBuffer): boolean
        engine.RegisterFunction(fsObj, "writeFileBytes", args =>
        {
            if (args.Length < 2) return false;
            string? path = engine.GetString(args[0]);
            if (path is null) return false;
            path = ResolvePath(path, basePath);
            IntPtr bufPtr = QuickJSNative.QJS_GetArrayBuffer(ctx, out UIntPtr sizePtr, args[1]);
            if (bufPtr == IntPtr.Zero) return false;
            var size = sizePtr.ToUInt64();
            byte[] data = new byte[size];
            System.Runtime.InteropServices.Marshal.Copy(bufPtr, data, 0, (int)size);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, data);
            return true;
        }, 2);

        // __fs.appendFile(path: string, content: string, encoding?: string): boolean
        engine.RegisterFunction(fsObj, "appendFile", args =>
        {
            if (args.Length < 2) return false;
            string? path = engine.GetString(args[0]);
            string? content = engine.GetString(args[1]);
            if (path is null || content is null) return false;
            path = ResolvePath(path, basePath);
            string encoding = args.Length > 2 ? (engine.GetString(args[2]) ?? "utf-8") : "utf-8";
            File.AppendAllText(path, content, GetEncoding(encoding));
            return true;
        }, 2);

        // __fs.exists(path: string): boolean
        engine.RegisterFunction(fsObj, "exists", args =>
        {
            if (args.Length < 1) return false;
            string? path = engine.GetString(args[0]);
            if (path is null) return false;
            path = ResolvePath(path, basePath);
            return File.Exists(path) || Directory.Exists(path);
        }, 1);

        // __fs.isFile(path: string): boolean
        engine.RegisterFunction(fsObj, "isFile", args =>
        {
            if (args.Length < 1) return false;
            string? path = engine.GetString(args[0]);
            if (path is null) return false;
            path = ResolvePath(path, basePath);
            return File.Exists(path);
        }, 1);

        // __fs.isDirectory(path: string): boolean
        engine.RegisterFunction(fsObj, "isDirectory", args =>
        {
            if (args.Length < 1) return false;
            string? path = engine.GetString(args[0]);
            if (path is null) return false;
            path = ResolvePath(path, basePath);
            return Directory.Exists(path);
        }, 1);

        // __fs.remove(path: string): boolean
        engine.RegisterFunction(fsObj, "remove", args =>
        {
            if (args.Length < 1) return false;
            string? path = engine.GetString(args[0]);
            if (path is null) return false;
            path = ResolvePath(path, basePath);
            if (File.Exists(path))
            {
                File.Delete(path);
                return true;
            }
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
                return true;
            }
            return false;
        }, 1);

        // __fs.rename(oldPath: string, newPath: string): boolean
        engine.RegisterFunction(fsObj, "rename", args =>
        {
            if (args.Length < 2) return false;
            string? oldPath = engine.GetString(args[0]);
            string? newPath = engine.GetString(args[1]);
            if (oldPath is null || newPath is null) return false;
            oldPath = ResolvePath(oldPath, basePath);
            newPath = ResolvePath(newPath, basePath);
            if (File.Exists(oldPath))
            {
                File.Move(oldPath, newPath);
                return true;
            }
            if (Directory.Exists(oldPath))
            {
                Directory.Move(oldPath, newPath);
                return true;
            }
            return false;
        }, 2);

        // __fs.copy(src: string, dst: string): boolean
        engine.RegisterFunction(fsObj, "copy", args =>
        {
            if (args.Length < 2) return false;
            string? src = engine.GetString(args[0]);
            string? dst = engine.GetString(args[1]);
            if (src is null || dst is null) return false;
            src = ResolvePath(src, basePath);
            dst = ResolvePath(dst, basePath);
            if (!File.Exists(src)) return false;
            var dir = Path.GetDirectoryName(dst);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.Copy(src, dst, overwrite: true);
            return true;
        }, 2);

        // __fs.mkdir(path: string): boolean
        engine.RegisterFunction(fsObj, "mkdir", args =>
        {
            if (args.Length < 1) return false;
            string? path = engine.GetString(args[0]);
            if (path is null) return false;
            path = ResolvePath(path, basePath);
            Directory.CreateDirectory(path);
            return true;
        }, 1);

        // __fs.readDir(path: string): string[] (JSON array)
        engine.RegisterFunction(fsObj, "readDir", args =>
        {
            if (args.Length < 1) return "[]";
            string? path = engine.GetString(args[0]);
            if (path is null) return "[]";
            path = ResolvePath(path, basePath);
            if (!Directory.Exists(path)) return "[]";
            var entries = Directory.GetFileSystemEntries(path);
            var sb = new StringBuilder("[");
            for (int i = 0; i < entries.Length; i++)
            {
                if (i > 0) sb.Append(',');
                var name = Path.GetFileName(entries[i]);
                sb.Append('"');
                sb.Append(name.Replace("\\", "\\\\").Replace("\"", "\\\""));
                sb.Append('"');
            }
            sb.Append(']');
            return sb.ToString();
        }, 1);

        // __fs.stat(path: string): object | null (as JSON string)
        engine.RegisterFunction(fsObj, "stat", args =>
        {
            if (args.Length < 1) return null;
            string? path = engine.GetString(args[0]);
            if (path is null) return null;
            path = ResolvePath(path, basePath);
            if (File.Exists(path))
            {
                var info = new FileInfo(path);
                return $"{{\"size\":{info.Length}," +
                       $"\"isFile\":true,\"isDirectory\":false," +
                       $"\"created\":\"{info.CreationTimeUtc:O}\"," +
                       $"\"modified\":\"{info.LastWriteTimeUtc:O}\"," +
                       $"\"accessed\":\"{info.LastAccessTimeUtc:O}\"}}";
            }
            if (Directory.Exists(path))
            {
                var info = new DirectoryInfo(path);
                return $"{{\"size\":0," +
                       $"\"isFile\":false,\"isDirectory\":true," +
                       $"\"created\":\"{info.CreationTimeUtc:O}\"," +
                       $"\"modified\":\"{info.LastWriteTimeUtc:O}\"," +
                       $"\"accessed\":\"{info.LastAccessTimeUtc:O}\"}}";
            }
            return null;
        }, 1);

        // __fs.getcwd(): string
        engine.RegisterFunction(fsObj, "getcwd", args =>
        {
            return Directory.GetCurrentDirectory();
        }, 0);

        // Set the __fs global object
        var global = QuickJSNative.QJS_GetGlobalObject(ctx);
        QuickJSNative.QJS_SetPropertyStr(ctx, global, "__fs", fsObj);
        QuickJSNative.QJS_FreeValue(ctx, global);

        // Install a JS wrapper that provides a cleaner API with parsed readDir/stat
        engine.Eval(
            """
            globalThis.fs = {
                readFile: (path, encoding) => __fs.readFile(path, encoding),
                readFileBytes: (path) => __fs.readFileBytes(path),
                writeFile: (path, content, encoding) => __fs.writeFile(path, content, encoding),
                writeFileBytes: (path, buffer) => __fs.writeFileBytes(path, buffer),
                appendFile: (path, content, encoding) => __fs.appendFile(path, content, encoding),
                exists: (path) => __fs.exists(path),
                isFile: (path) => __fs.isFile(path),
                isDirectory: (path) => __fs.isDirectory(path),
                remove: (path) => __fs.remove(path),
                rename: (oldPath, newPath) => __fs.rename(oldPath, newPath),
                copy: (src, dst) => __fs.copy(src, dst),
                mkdir: (path) => __fs.mkdir(path),
                readDir: (path) => JSON.parse(__fs.readDir(path)),
                stat: (path) => { const s = __fs.stat(path); return s ? JSON.parse(s) : null; },
                getcwd: () => __fs.getcwd(),
            };
            """, "<fs-init>");
    }

    private static string ResolvePath(string? path, string? basePath)
    {
        ArgumentNullException.ThrowIfNull(path, nameof(path));
        if (basePath != null)
        {
            // Sandboxed: resolve relative to basePath, prevent escape
            var full = Path.GetFullPath(Path.Combine(basePath, path));
            var baseNorm = Path.GetFullPath(basePath);
            if (!full.StartsWith(baseNorm, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException($"Path '{path}' escapes the sandbox.");
            return full;
        }
        return Path.GetFullPath(path);
    }

    private static Encoding GetEncoding(string name)
    {
        switch (name.ToLowerInvariant())
        {
            case "utf-8":
            case "utf8":
                return Encoding.UTF8;
            case "ascii":
                return Encoding.ASCII;
            case "unicode":
            case "utf-16":
            case "utf16":
                return Encoding.Unicode;
            case "utf-32":
            case "utf32":
                return Encoding.UTF32;
            case "latin1":
                return Encoding.UTF32;
            default:
                return Encoding.UTF8;
        }
    }
}
