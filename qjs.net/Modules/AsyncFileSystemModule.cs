using QuickJsNet.Core;
using QuickJSNet.Bindings;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace QuickJsNet.Modules;

/// <summary>
/// Registers asynchronous file system functions into the QuickJS engine.
/// Each IO operation runs on a ThreadPool thread and settles a JS Promise
/// via the EventLoop callback queue.
/// Accessible from JavaScript via the <c>fsAsync</c> global object.
/// </summary>
internal static class AsyncFileSystemModule
{
    /// <summary>
    /// Install async file system functions.
    /// Available in JS as: fsAsync.readFile(path), fsAsync.writeFile(path, content), etc.
    /// All methods return a Promise.
    /// </summary>
    public static void Install(QuickJSRuntime engine, string? basePath = null)
    {
        var ctx = engine.Context;
        var obj = QuickJSNative.QJS_NewObject(ctx);

        // ---- readFile(path, encoding, resolve, reject) ----
        engine.RegisterFunction(obj, "readFileImpl", args =>
        {
            string? path = engine.GetString(args[0]);
            string encoding = engine.GetString(args[1]) ?? "utf-8";
            var resolve = engine.DupValue(args[2]);
            var reject = engine.DupValue(args[3]);

            engine.Promise(resolve, reject, () =>
            {
                string resolved = ResolvePath(path, basePath);
                if (!File.Exists(resolved))
                    throw new FileNotFoundException("File not found: " + path);
                return File.ReadAllText(resolved, GetEncoding(encoding));
            });
            return null;
        }, 4);

        // ---- readFileBytes(path, resolve, reject) ----
        engine.RegisterFunction(obj, "readFileBytesImpl", args =>
        {
            string? path = engine.GetString(args[0]);
            var resolve = engine.DupValue(args[1]);
            var reject = engine.DupValue(args[2]);

            engine.Promise(resolve, reject, () =>
            {
                string resolved = ResolvePath(path, basePath);
                if (!File.Exists(resolved))
                    throw new FileNotFoundException("File not found: " + path);
                return File.ReadAllBytes(resolved);
            });
            return null;
        }, 3);

        // ---- writeFile(path, content, encoding, resolve, reject) ----
        engine.RegisterFunction(obj, "writeFileImpl", args =>
        {
            string? path = engine.GetString(args[0]);
            string? content = engine.GetString(args[1]);
            string encoding = engine.GetString(args[2]) ?? "utf-8";
            var resolve = engine.DupValue(args[3]);
            var reject = engine.DupValue(args[4]);

            engine.Promise(resolve, reject, () =>
            {
                if (content is null)
                    throw new ArgumentException("Path and content are required");
                string resolved = ResolvePath(path, basePath);
                var dir = Path.GetDirectoryName(resolved);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(resolved, content, GetEncoding(encoding));
                return true;
            });
            return null;
        }, 5);

        // ---- writeFileBytes(path, buffer, resolve, reject) ----
        // Buffer data must be copied synchronously before the async dispatch.
        engine.RegisterFunction(obj, "writeFileBytesImpl", args =>
        {
            string? path = engine.GetString(args[0]);
            // Extract buffer bytes NOW on the JS thread
            IntPtr bufPtr = QuickJSNative.QJS_GetArrayBuffer(ctx, out UIntPtr sizePtr, args[1]);
            byte[]? data = null;
            if (bufPtr != IntPtr.Zero)
            {
                int size = (int)sizePtr.ToUInt64();
                data = new byte[size];
                Marshal.Copy(bufPtr, data, 0, size);
            }
            var resolve = engine.DupValue(args[2]);
            var reject = engine.DupValue(args[3]);

            var capturedData = data;
            engine.Promise(resolve, reject, () =>
            {
                if (path is null)
                    throw new ArgumentException("Path is required");
                if (capturedData is null)
                    throw new ArgumentException("Buffer is required");
                string resolved = ResolvePath(path, basePath);
                var dir = Path.GetDirectoryName(resolved);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllBytes(resolved, capturedData);
                return true;
            });
            return null;
        }, 4);

        // ---- appendFile(path, content, encoding, resolve, reject) ----
        engine.RegisterFunction(obj, "appendFileImpl", args =>
        {
            string? path = engine.GetString(args[0]);
            string? content = engine.GetString(args[1]);
            string encoding = engine.GetString(args[2]) ?? "utf-8";
            var resolve = engine.DupValue(args[3]);
            var reject = engine.DupValue(args[4]);

            engine.Promise(resolve, reject, () =>
            {
                if (path is null || content is null)
                    throw new ArgumentException("Path and content are required");
                string resolved = ResolvePath(path, basePath);
                File.AppendAllText(resolved, content, GetEncoding(encoding));
                return true;
            });
            return null;
        }, 5);

        // ---- exists(path, resolve, reject) ----
        engine.RegisterFunction(obj, "existsImpl", args =>
        {
            string? path = engine.GetString(args[0]);
            var resolve = engine.DupValue(args[1]);
            var reject = engine.DupValue(args[2]);

            engine.Promise(resolve, reject, () =>
            {
                if (path is null) return (object)false;
                string resolved = ResolvePath(path, basePath);
                return (object)(File.Exists(resolved) || Directory.Exists(resolved));
            });
            return null;
        }, 3);

        // ---- isFile(path, resolve, reject) ----
        engine.RegisterFunction(obj, "isFileImpl", args =>
        {
            string? path = engine.GetString(args[0]);
            var resolve = engine.DupValue(args[1]);
            var reject = engine.DupValue(args[2]);

            engine.Promise(resolve, reject, () =>
            {
                if (path is null) return (object)false;
                string resolved = ResolvePath(path, basePath);
                return (object)File.Exists(resolved);
            });
            return null;
        }, 3);

        // ---- isDirectory(path, resolve, reject) ----
        engine.RegisterFunction(obj, "isDirectoryImpl", args =>
        {
            string? path = engine.GetString(args[0]);
            var resolve = engine.DupValue(args[1]);
            var reject = engine.DupValue(args[2]);

            engine.Promise(resolve, reject, () =>
            {
                if (path is null) return (object)false;
                string resolved = ResolvePath(path, basePath);
                return (object)Directory.Exists(resolved);
            });
            return null;
        }, 3);

        // ---- remove(path, resolve, reject) ----
        engine.RegisterFunction(obj, "removeImpl", args =>
        {
            string? path = engine.GetString(args[0]);
            var resolve = engine.DupValue(args[1]);
            var reject = engine.DupValue(args[2]);

            engine.Promise(resolve, reject, () =>
            {
                if (path is null) return (object)false;
                string resolved = ResolvePath(path, basePath);
                if (File.Exists(resolved))
                {
                    File.Delete(resolved);
                    return (object)true;
                }
                if (Directory.Exists(resolved))
                {
                    Directory.Delete(resolved, true);
                    return (object)true;
                }
                return (object)false;
            });
            return null;
        }, 3);

        // ---- rename(oldPath, newPath, resolve, reject) ----
        engine.RegisterFunction(obj, "renameImpl", args =>
        {
            string? oldPath = engine.GetString(args[0]);
            string? newPath = engine.GetString(args[1]);
            var resolve = engine.DupValue(args[2]);
            var reject = engine.DupValue(args[3]);

            engine.Promise(resolve, reject, () =>
            {
                if (oldPath is null || newPath is null)
                    throw new ArgumentException("Both oldPath and newPath are required");
                string resolvedOld = ResolvePath(oldPath, basePath);
                string resolvedNew = ResolvePath(newPath, basePath);
                if (File.Exists(resolvedOld))
                {
                    File.Move(resolvedOld, resolvedNew);
                    return (object)true;
                }
                if (Directory.Exists(resolvedOld))
                {
                    Directory.Move(resolvedOld, resolvedNew);
                    return (object)true;
                }
                return (object)false;
            });
            return null;
        }, 4);

        // ---- copy(src, dst, resolve, reject) ----
        engine.RegisterFunction(obj, "copyImpl", args =>
        {
            string? src = engine.GetString(args[0]);
            string? dst = engine.GetString(args[1]);
            var resolve = engine.DupValue(args[2]);
            var reject = engine.DupValue(args[3]);

            engine.Promise(resolve, reject, () =>
            {
                if (src is null || dst is null)
                    throw new ArgumentException("Both src and dst are required");
                string resolvedSrc = ResolvePath(src, basePath);
                string resolvedDst = ResolvePath(dst, basePath);
                if (!File.Exists(resolvedSrc))
                    throw new FileNotFoundException("Source file not found: " + src);
                var dir = Path.GetDirectoryName(resolvedDst);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.Copy(resolvedSrc, resolvedDst, overwrite: true);
                return (object)true;
            });
            return null;
        }, 4);

        // ---- mkdir(path, resolve, reject) ----
        engine.RegisterFunction(obj, "mkdirImpl", args =>
        {
            string? path = engine.GetString(args[0]);
            var resolve = engine.DupValue(args[1]);
            var reject = engine.DupValue(args[2]);

            engine.Promise(resolve, reject, () =>
            {
                if (path is null)
                    throw new ArgumentException("Path is required");
                string resolved = ResolvePath(path, basePath);
                Directory.CreateDirectory(resolved);
                return (object)true;
            });
            return null;
        }, 3);

        // ---- readDir(path, resolve, reject) → JSON string ----
        engine.RegisterFunction(obj, "readDirImpl", args =>
        {
            string? path = engine.GetString(args[0]);
            var resolve = engine.DupValue(args[1]);
            var reject = engine.DupValue(args[2]);

            engine.Promise(resolve, reject, () =>
            {
                if (path is null) return "[]";
                string resolved = ResolvePath(path, basePath);
                if (!Directory.Exists(resolved)) return "[]";
                var entries = Directory.GetFileSystemEntries(resolved);
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
            });
            return null;
        }, 3);

        // ---- stat(path, resolve, reject) → JSON string | null ----
        engine.RegisterFunction(obj, "statImpl", args =>
        {
            string? path = engine.GetString(args[0]);
            var resolve = engine.DupValue(args[1]);
            var reject = engine.DupValue(args[2]);

            engine.Promise(resolve, reject, () =>
            {
                if (path is null) return null;
                string resolved = ResolvePath(path, basePath);
                if (File.Exists(resolved))
                {
                    var info = new FileInfo(resolved);
                    return $"{{\"size\":{info.Length}," +
                           $"\"isFile\":true,\"isDirectory\":false," +
                           $"\"created\":\"{info.CreationTimeUtc:O}\"," +
                           $"\"modified\":\"{info.LastWriteTimeUtc:O}\"," +
                           $"\"accessed\":\"{info.LastAccessTimeUtc:O}\"}}";
                }
                if (Directory.Exists(resolved))
                {
                    var info = new DirectoryInfo(resolved);
                    return $"{{\"size\":0," +
                           $"\"isFile\":false,\"isDirectory\":true," +
                           $"\"created\":\"{info.CreationTimeUtc:O}\"," +
                           $"\"modified\":\"{info.LastWriteTimeUtc:O}\"," +
                           $"\"accessed\":\"{info.LastAccessTimeUtc:O}\"}}";
                }
                return null;
            });
            return null;
        }, 3);

        // ---- getcwd(resolve, reject) ----
        engine.RegisterFunction(obj, "getcwdImpl", args =>
        {
            var resolve = engine.DupValue(args[0]);
            var reject = engine.DupValue(args[1]);

            engine.Promise(resolve, reject, Directory.GetCurrentDirectory);
            return null;
        }, 2);

        // ---- temp dir ----
        engine.RegisterFunction(obj, "tempDirImpl", args =>
        {
            var resolve = engine.DupValue(args[0]);
            var reject = engine.DupValue(args[1]);
            engine.Promise(resolve, reject, Path.GetTempPath);
            return null;
        }, 2);

        //// ---- getOpenFileName dialog ----
        //engine.RegisterFunction(obj, "openFileDialogImpl", args =>
        //{
        //    string title = engine.GetString(args[0]);
        //    string initialDir = engine.GetString(args[1]);
        //    string filter = engine.GetString(args[2]);
        //    bool multiSelect = engine.GetInt32(args[3]) != 0;

        //    var resolve = engine.DupValue(args[4]);
        //    var reject = engine.DupValue(args[5]);

        //    engine.Promise(resolve, reject, () =>
        //    {
        //        string selectedFile = null;

        //        // 创建一个单独的 STA 线程来运行 Windows Forms 控件
        //        Thread staThread = new Thread(() =>
        //        {
        //            using var dialog = new System.Windows.Forms.OpenFileDialog();
        //            dialog.Title = title ?? "Select a file";
        //            dialog.Filter = filter ?? "All files (*.*)|*.*";
        //            dialog.Multiselect = multiSelect;

        //            if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir))
        //                dialog.InitialDirectory = initialDir;

        //            try
        //            {
        //                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        //                {
        //                    selectedFile = string.Join("|", dialog.FileNames);
        //                }
        //            }
        //            catch (Exception ex)
        //            {
        //                selectedFile = string.Empty;
        //                Debug.WriteLine(ex);
        //            }
        //        });

        //        staThread.SetApartmentState(ApartmentState.STA); // 设置为单线程单元
        //        staThread.Start();
        //        staThread.Join(); // 阻塞当前 ThreadPool 线程，直到用户关闭对话框

        //        return selectedFile;
        //    });
        //    return null;
        //}, 6);

        //// ---- getSaveFileName dialog ----
        //engine.RegisterFunction(obj, "saveFileDialogImpl", args =>
        //{
        //    string? title = engine.GetString(args[0]);
        //    string? initialDir = engine.GetString(args[1]);
        //    string? filter = engine.GetString(args[2]);
        //    var resolve = engine.DupValue(args[3]);
        //    var reject = engine.DupValue(args[4]);
        //    engine.Promise(resolve, reject, () =>
        //    {
        //        string? selectedFile = null;
        //        // 创建一个单独的 STA 线程来运行 Windows Forms 控件
        //        Thread staThread = new(() =>
        //        {
        //            using var dialog = new System.Windows.Forms.SaveFileDialog();
        //            dialog.Title = title ?? "Select a file";
        //            dialog.Filter = filter ?? "All files (*.*)|*.*";
        //            if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir))
        //                dialog.InitialDirectory = initialDir;
        //            try
        //            {
        //                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        //                {
        //                    selectedFile = dialog.FileName;
        //                }
        //            }
        //            catch (Exception ex)
        //            {
        //                selectedFile = string.Empty;
        //                Debug.WriteLine(ex);
        //            }
        //        });
        //        staThread.SetApartmentState(ApartmentState.STA); // 设置为单线程单元
        //        staThread.Start();
        //        staThread.Join(); // 阻塞当前 ThreadPool 线程，直到用户关闭对话框
        //        return selectedFile;
        //    });
        //    return null;
        //}, 5);

        // Set the __fsAsync global object
        var global = QuickJSNative.QJS_GetGlobalObject(ctx);
        QuickJSNative.QJS_SetPropertyStr(ctx, global, "__fsAsync", obj);
        QuickJSNative.QJS_FreeValue(ctx, global);

        // Install JS Promise wrappers that provide a clean API
        engine.Eval(
            """
            globalThis.fsAsync = {
                readFile: (path, encoding) => new Promise((resolve, reject) => {
                    __fsAsync.readFileImpl(path, encoding || 'utf-8', resolve, reject);
                }),
                
                readFileBytes: (path) => new Promise((resolve, reject) => {
                    __fsAsync.readFileBytesImpl(path, resolve, reject);
                }),
                
                writeFile: (path, content, encoding) => new Promise((resolve, reject) => {
                    __fsAsync.writeFileImpl(path, content, encoding || 'utf-8', resolve, reject);
                }),
                
                writeFileBytes: (path, buffer) => new Promise((resolve, reject) => {
                    __fsAsync.writeFileBytesImpl(path, buffer, resolve, reject);
                }),
                
                appendFile: (path, content, encoding) => new Promise((resolve, reject) => {
                    __fsAsync.appendFileImpl(path, content, encoding || 'utf-8', resolve, reject);
                }),
                
                exists: (path) => new Promise((resolve, reject) => {
                    __fsAsync.existsImpl(path, resolve, reject);
                }),
                
                isFile: (path) => new Promise((resolve, reject) => {
                    __fsAsync.isFileImpl(path, resolve, reject);
                }),
                
                isDirectory: (path) => new Promise((resolve, reject) => {
                    __fsAsync.isDirectoryImpl(path, resolve, reject);
                }),
                
                remove: (path) => new Promise((resolve, reject) => {
                    __fsAsync.removeImpl(path, resolve, reject);
                }),
                
                rename: (oldPath, newPath) => new Promise((resolve, reject) => {
                    __fsAsync.renameImpl(oldPath, newPath, resolve, reject);
                }),
                
                copy: (src, dst) => new Promise((resolve, reject) => {
                    __fsAsync.copyImpl(src, dst, resolve, reject);
                }),
                
                mkdir: (path) => new Promise((resolve, reject) => {
                    __fsAsync.mkdirImpl(path, resolve, reject);
                }),
                
                readDir: (path) => new Promise((resolve, reject) => {
                    __fsAsync.readDirImpl(path, resolve, reject);
                }).then(s => JSON.parse(s)),
                
                stat: (path) => new Promise((resolve, reject) => {
                    __fsAsync.statImpl(path, resolve, reject);
                }).then(s => s !== null ? JSON.parse(s) : null),
                
                getcwd: () => new Promise((resolve, reject) => {
                    __fsAsync.getcwdImpl(resolve, reject);
                }),
                
                tempDir: () => new Promise((resolve, reject) => {
                    __fsAsync.tempDirImpl(resolve, reject);
                }),

                getOpenFileName: (options) => new Promise((resolve, reject) => {
                    __fsAsync.openFileDialogImpl(
                        options?.title || 'Select a file',
                        options?.initialDir || '',
                        options?.filter || 'All files (*.*)|*.*',
                        options?.multiSelect ? 1 : 0,
                        resolve, reject);
                }),

                getSaveFileName: (options) => new Promise((resolve, reject) => {
                    __fsAsync.saveFileDialogImpl(
                        options?.title || 'Select a file',
                        options?.initialDir || '',
                        options?.filter || 'All files (*.*)|*.*',
                        resolve, reject);
                }),
            };
            """, "<fs-async-init>");
    }

    private static string ResolvePath(string? path, string? basePath)
    {
        ArgumentNullException.ThrowIfNull(path, nameof(path));
        if (basePath != null)
        {
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
                return Encoding.GetEncoding("iso-8859-1");
            default:
                return Encoding.UTF8;
        }
    }
}
