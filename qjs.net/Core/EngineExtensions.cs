using QuickJsNet.Modules;
using QuickJsNet.Utils;

namespace QuickJsNet.Core;

internal static class EngineExtensions
{
    public static QuickJSRuntime InstallAllModules(this QuickJSRuntime engine)
    {
        EncoderModule.Install(engine);
        FileSystemModule.Install(engine);
        AsyncFileSystemModule.Install(engine);
        FetchModule.Install(engine);
        WindowsSimple.Install(engine);
        return engine;
    }

    public static QuickJSRuntime InstallEncoderModule(this QuickJSRuntime engine)
    {
        EncoderModule.Install(engine);
        return engine;
    }

    public static QuickJSRuntime InstallFileSystemModule(this QuickJSRuntime engine)
    {
        FileSystemModule.Install(engine);
        return engine;
    }

    public static QuickJSRuntime InstallAsyncFileSystemModule(this QuickJSRuntime engine)
    {
        AsyncFileSystemModule.Install(engine);
        return engine;
    }

    public static QuickJSRuntime InstallFetchModule(this QuickJSRuntime engine)
    {
        FetchModule.Install(engine);
        return engine;
    }

    public static QuickJSRuntime InstallWindowsSimpleModule(this QuickJSRuntime engine)
    {
        WindowsSimple.Install(engine);
        return engine;
    }

    public static QuickJSRuntime InstallLocalStorageModule(this QuickJSRuntime engine, string storeFile)
    {
        var kvs = SharedLogKvStorage.Open(storeFile);
        engine.RegisterGlobalFunction("__localStorageGetImpl", args =>
        {
            var key = engine.GetString(args[0]);
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }
            return kvs.GetAsync(key).GetAwaiter().GetResult();
        }, 1);

        engine.RegisterGlobalFunction("__localStorageSetImpl", args =>
        {
            var key = engine.GetString(args[0]);
            var value = engine.GetString(args[1]);
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }
            kvs.SetAsync(key, value ?? "").GetAwaiter().GetResult();
            return null;
        }, 2);

        engine.RegisterGlobalFunction("__localStorageRemoveImpl", args =>
        {
            var key = engine.GetString(args[0]);
            if (string.IsNullOrEmpty(key))
            {
                return null;
            }
            kvs.RemoveAsync(key).GetAwaiter().GetResult();
            return null;
        }, 1);

        engine.RegisterGlobalFunction("__localStorageClearImpl", args =>
        {
            kvs.ClearAsync().GetAwaiter().GetResult();
            return null;
        }, 0);

        engine.RegisterGlobalFunction("__localStorageGetKeyImpl", args =>
        {
            var index = engine.GetInt32(args[0]);
            var keys = kvs.GetKeysAsync().GetAwaiter().GetResult();
            return index >= 0 && index < keys.Count ? keys.Skip(index).FirstOrDefault() : null;
        }, 1);

        engine.RegisterGlobalFunction("__localStorageLengthImpl", args =>
        {
            var keys = kvs.GetKeysAsync().GetAwaiter().GetResult();
            return keys.Count;
        }, 0);

        engine.RegisterGlobalFunction("__localStorageHasKeyImpl", args =>
        {
            var key = engine.GetString(args[0]);
            if (string.IsNullOrEmpty(key))
            {
                return false;
            }
            return kvs.HasKeyAsync(key).GetAwaiter().GetResult();
        }, 1);

        engine.RegisterGlobalFunction("__localStorageKeysImpl", args =>
        {
            var keys = kvs.GetKeysAsync().GetAwaiter().GetResult().ToArray();
            return System.Text.Json.JsonSerializer.Serialize(keys);
        }, 0);

        engine.RegisterGlobalFunction("__localStorageFindKeysImpl", args =>
        {
            var keyword = engine.GetString(args[0]);
            var keys = kvs.FindKeysAsync(keyword!).GetAwaiter().GetResult().ToArray();
            return System.Text.Json.JsonSerializer.Serialize(keys);
        }, 1);

        engine.Eval(@"
                (function() {
                    const global = this;
                    const localStorage = {
                        getItem(key) {
                            return __localStorageGetImpl(key);
                        },
                        setItem(key, value) {
                            __localStorageSetImpl(key, value);
                        },
                        removeItem(key) {
                            __localStorageRemoveImpl(key);
                        },
                        clear() {
                            __localStorageClearImpl();
                        },
                        key(index) {
                            return __localStorageGetKeyImpl(index);
                        },
                        get length() {
                            return __localStorageLengthImpl();
                        },
                        hasKey(key) {
                            return __localStorageHasKeyImpl(key);
                        },
                        keys() {
                            const jsonStr = __localStorageKeysImpl();
                            return JSON.parse(jsonStr); 
                        },
                        findKeys(keyword) {
                            if (!keyword) return [];
                            const jsonStr = __localStorageFindKeysImpl(keyword);
                            return JSON.parse(jsonStr); 
                        }
                    };
                    global.localStorage = localStorage;
                })();", "<localStorage-init>");

        return engine;
    }
}
