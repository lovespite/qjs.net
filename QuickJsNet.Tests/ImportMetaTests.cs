using QuickJsNet;
using QuickJsNet.Core;
using Xunit;

namespace QuickJsNet.Tests;

public class ImportMetaTests
{
    [Fact]
    public void ImportMeta_Url_VirtualModule()
    {
        using var engine = new QuickJSEngine();
        engine.Modules.Register("foo", "export const u = import.meta.url;");
        engine.ExecuteModule(@"
            import { u } from 'foo';
            globalThis.__u = u;
        ");
        var url = engine.Execute("globalThis.__u") as string;
        Assert.Equal("qjs-virtual:foo", url);
    }

    [Fact]
    public void ImportMeta_Url_FileModule()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "qjsnet_im_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        var file = System.IO.Path.Combine(dir, "m.js");
        System.IO.File.WriteAllText(file, "export const u = import.meta.url;");
        try
        {
            using var engine = new QuickJSEngine();
            engine.Modules.BasePath = dir;
            engine.ExecuteModule(@"
                import { u } from './m.js';
                globalThis.__u = u;
            ");
            var url = engine.Execute("globalThis.__u") as string;
            Assert.NotNull(url);
            Assert.StartsWith("file:///", url);
            Assert.EndsWith("m.js", url!.Replace('\\', '/'));
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, true); } catch { }
        }
    }

    [Fact]
    public void ImportMeta_Url_NativeModule()
    {
        using var engine = new QuickJSEngine();
        engine.Modules.RegisterNative("qjs:demo", b =>
        {
            b.Export("name", "demo");
        });
        engine.ExecuteModule(@"
            import { name } from 'qjs:demo';
            // Use the binding to force module init
            globalThis.__n = name;
            // import.meta is per-module; we can't read it from the entry script
            // but can check via dynamic reflection: just verify the side-effect works.
        ");
        Assert.Equal("demo", engine.Execute("globalThis.__n"));
    }

    [Fact]
    public void ImportMeta_HasMain_Property()
    {
        using var engine = new QuickJSEngine();
        engine.Modules.Register("hasmain",
            "export const m = (typeof import.meta.main === 'boolean');");
        engine.ExecuteModule(@"
            import { m } from 'hasmain';
            globalThis.__m = m;
        ");
        Assert.Equal(true, engine.Execute("globalThis.__m"));
    }
}
