using QuickJsNet;
using QuickJsNet.Core;

namespace QuickJsNet.Tests;

/// <summary>
/// Tests for ES module (import / export) support.
/// </summary>
public class EsModuleTests
{
    [Fact]
    public void VirtualModule_NamedExport_IsImportable()
    {
        using var engine = new QuickJSEngine();
        engine.Modules.Register("math", "export const add = (a,b) => a + b;");

        engine.ExecuteModule("import { add } from 'math'; globalThis.__r = add(2, 3);");

        Assert.Equal(5, engine.GetGlobal("__r"));
    }

    [Fact]
    public void VirtualModule_DefaultExport_IsImportable()
    {
        using var engine = new QuickJSEngine();
        engine.Modules.Register("hello", "export default function (n) { return 'hi ' + n; }");

        engine.ExecuteModule("import h from 'hello'; globalThis.__r = h('alice');");

        Assert.Equal("hi alice", engine.GetGlobal("__r"));
    }

    [Fact]
    public void Import_ReturnsNamespaceObject()
    {
        using var engine = new QuickJSEngine();
        engine.Modules.Register("util", "export const x = 42; export const y = 'z';");

        var ns = engine.Import("util");

        // Object values are surfaced as JSON strings (engine convention).
        Assert.NotNull(ns);
        var json = ns!.ToString()!;
        Assert.Contains("\"x\":42", json);
        Assert.Contains("\"y\":\"z\"", json);
    }

    [Fact]
    public void Module_ChainedImports_Resolve()
    {
        using var engine = new QuickJSEngine();
        engine.Modules.Register("a", "export const v = 'A';");
        engine.Modules.Register("b", "import { v } from 'a'; export const w = v + 'B';");

        engine.ExecuteModule("import { w } from 'b'; globalThis.__r = w;");

        Assert.Equal("AB", engine.GetGlobal("__r"));
    }

    [Fact]
    public void Module_TopLevelAwait_Works()
    {
        using var engine = new QuickJSEngine();
        engine.Modules.Register("delayed",
            "const v = await Promise.resolve(123); export const value = v;");

        engine.ExecuteModule("import { value } from 'delayed'; globalThis.__r = value;");
        Assert.Equal(123, engine.GetGlobal("__r"));
    }

    [Fact]
    public void DynamicImport_Works()
    {
        using var engine = new QuickJSEngine();
        engine.Modules.Register("dyn", "export const ping = () => 'pong';");

        engine.ExecuteModule(
            "const m = await import('dyn'); globalThis.__r = m.ping();");
        Assert.Equal("pong", engine.GetGlobal("__r"));
    }

    [Fact]
    public void Module_MissingImport_Throws()
    {
        using var engine = new QuickJSEngine();
        Assert.Throws<QuickJSException>(() =>
            engine.ExecuteModule("import { x } from 'does-not-exist';"));
    }

    [Fact]
    public void Module_SyntaxError_Throws()
    {
        using var engine = new QuickJSEngine();
        engine.Modules.Register("bad", "export const = ;");
        Assert.Throws<QuickJSException>(() =>
            engine.ExecuteModule("import { x } from 'bad';"));
    }

    [Fact]
    public void FileSystemModule_LoadsFromBasePath()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "qjsnet-esm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            File.WriteAllText(Path.Combine(tmp, "lib.js"), "export const v = 99;");
            using var engine = new QuickJSEngine();
            engine.Modules.BasePath = tmp;

            engine.ExecuteModule("import { v } from './lib.js'; globalThis.__r = v;");
            Assert.Equal(99, engine.GetGlobal("__r"));
        }
        finally
        {
            try { Directory.Delete(tmp, true); } catch { }
        }
    }

    [Fact]
    public void CustomResolver_TakesPrecedenceOverVirtual()
    {
        using var engine = new QuickJSEngine();
        engine.Modules.Register("greet", "export const v = 'virtual';");
        engine.Modules.Resolver = name => name == "greet" ? "export const v = 'custom';" : null;

        engine.ExecuteModule("import { v } from 'greet'; globalThis.__r = v;");
        Assert.Equal("custom", engine.GetGlobal("__r"));
    }

    [Fact]
    public void Module_DedupesAcrossImports()
    {
        using var engine = new QuickJSEngine();
        // Module body runs once: counter increments only on first eval.
        engine.Modules.Register("counter",
            "globalThis.__count = (globalThis.__count|0) + 1; export const n = globalThis.__count;");

        engine.ExecuteModule("import { n } from 'counter'; globalThis.__a = n;");
        engine.ExecuteModule("import { n } from 'counter'; globalThis.__b = n;");

        Assert.Equal(1, engine.GetGlobal("__a"));
        Assert.Equal(1, engine.GetGlobal("__b"));
    }
}


public class LargeModuleTests
{
    private static string BuildLargeModule(int payloadBytes)
    {
        // Each entry contributes ~32 chars; pad to reach roughly payloadBytes.
        var sb = new System.Text.StringBuilder(payloadBytes + 1024);
        sb.Append("export const items = [\n");
        int n = payloadBytes / 32;
        for (int i = 0; i < n; i++)
        {
            sb.Append("  \"");
            sb.Append('x', 24);
            sb.Append("\",\n");
        }
        sb.Append("];\nexport const count = items.length;\nexport const marker = 'OK';\n");
        return sb.ToString();
    }

    [Fact]
    public void LargeModule_OneAndAHalfMiB_LoadsSuccessfully()
    {
        using var engine = new QuickJSEngine();
        var src = BuildLargeModule(1_572_864); // 1.5 MiB payload
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(src) > 1_048_576, "source must exceed legacy 1 MiB cap");
        engine.Modules.Register("big", src);

        engine.ExecuteModule("import { count, marker } from 'big'; globalThis.__c = count; globalThis.__m = marker;");

        Assert.Equal("OK", engine.GetGlobal("__m"));
        var c = System.Convert.ToInt32(engine.GetGlobal("__c"));
        Assert.True(c > 40_000, $"expected many items, got {c}");
    }

    [Fact]
    public void LargeModule_FiveMiB_LoadsSuccessfully()
    {
        using var engine = new QuickJSEngine();
        var src = BuildLargeModule(5 * 1024 * 1024);
        engine.Modules.Register("huge", src);

        engine.ExecuteModule("import { marker } from 'huge'; globalThis.__m = marker;");

        Assert.Equal("OK", engine.GetGlobal("__m"));
    }
}
