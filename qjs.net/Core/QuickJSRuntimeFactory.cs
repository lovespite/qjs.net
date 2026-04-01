namespace QuickJsNet.Core;

/// <summary>
/// 基于 QuickJS 的引擎工厂实现
/// </summary>
internal class QuickJSRuntimeFactory
{
    private readonly string _localStorageDbPath;
    public QuickJSRuntimeFactory(string localStorageDbPath)
    {
        _localStorageDbPath = localStorageDbPath;
    }

    public QuickJSRuntime CreateEngine()
    {
        var engine = new QuickJSRuntime()
            .InstallEncoderModule()
            .InstallFileSystemModule()
            .InstallAsyncFileSystemModule()
            .InstallFetchModule()
            .InstallWindowsSimpleModule()
            .InstallLocalStorageModule(_localStorageDbPath);
        return engine;
    }
}
