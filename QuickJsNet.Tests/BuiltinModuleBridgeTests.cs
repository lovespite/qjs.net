using QuickJsNet;

namespace QuickJsNet.Tests;

/// <summary>
/// Tests for <c>BuiltinAsModule</c> — re-exposing built-in modules
/// (fs, fetch, Buffer, ...) as importable <c>qjs:*</c> ES modules.
/// </summary>
public class BuiltinModuleBridgeTests
{
    private static QuickJSEngine NewEngine(bool only = false)
    {
        return new QuickJSEngine(o =>
        {
            o.BuiltinAsModule = true;
            o.BuiltinAsModuleOnly = only;
        });
    }

    [Fact]
    public void Buffer_IsImportable_AsDefault()
    {
        using var engine = NewEngine();
        engine.ExecuteModule(@"
            import Buffer from 'qjs:buffer';
            globalThis.__r = Buffer.from('hi').toString('utf8');
        ");
        Assert.Equal("hi", engine.GetGlobal("__r"));
    }

    [Fact]
    public void Fs_IsImportable_AsDefault()
    {
        using var engine = NewEngine();
        var tmp = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(tmp, "esm-bridged");
            var src = "import fs from 'qjs:fs'; globalThis.__r = fs.readFile("
                + System.Text.Json.JsonSerializer.Serialize(tmp, QuickJsNet.Tests.TestJsonContext.Default.String) + ");";
            engine.ExecuteModule(src);
            Assert.Equal("esm-bridged", engine.GetGlobal("__r"));
        }
        finally { System.IO.File.Delete(tmp); }
    }

    [Fact]
    public void TextEncoder_IsImportable()
    {
        using var engine = NewEngine();
        engine.ExecuteModule(@"
            import TextEncoder from 'qjs:textEncoder';
            const enc = new TextEncoder();
            globalThis.__r = enc.encode('ab').length;
        ");
        Assert.Equal(2, Convert.ToInt32(engine.GetGlobal("__r")));
    }

    [Fact]
    public void BuiltinAsModuleOnly_RemovesGlobals()
    {
        using var engine = NewEngine(only: true);
        // Global 'fs' should be gone, but import still works.
        engine.Execute("globalThis.__hasGlobalFs = (typeof fs !== 'undefined');");
        Assert.Equal(false, engine.GetGlobal("__hasGlobalFs"));

        var tmp = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(tmp, "only-mode");
            var src = "import fs from 'qjs:fs'; globalThis.__r = fs.readFile("
                + System.Text.Json.JsonSerializer.Serialize(tmp, QuickJsNet.Tests.TestJsonContext.Default.String) + ");";
            engine.ExecuteModule(src);
            Assert.Equal("only-mode", engine.GetGlobal("__r"));
        }
        finally { System.IO.File.Delete(tmp); }
    }

    [Fact]
    public void BuiltinAsModule_Disabled_ByDefault()
    {
        using var engine = new QuickJSEngine();
        var ex = Record.Exception(() =>
            engine.ExecuteModule("import Buffer from 'qjs:buffer';"));
        Assert.NotNull(ex);
    }
}
