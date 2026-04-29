using QuickJsNet;
using QuickJsNet.Core;

namespace QuickJsNet.Tests;

/// <summary>
/// Tests for native (C#-defined) ES modules registered via
/// <see cref="ModuleLoader.RegisterNative"/>.
/// </summary>
public class NativeModuleTests
{
    [Fact]
    public void NativeModule_ScalarExports_AreImportable()
    {
        using var engine = new QuickJSEngine();
        engine.Modules.RegisterNative("qjs:scalars", b => b
            .Export("answer", 42)
            .Export("pi", 3.14)
            .Export("greeting", "hello")
            .Export("flag", true)
            .Export("nothing", null));

        engine.ExecuteModule(@"
            import { answer, pi, greeting, flag, nothing } from 'qjs:scalars';
            globalThis.__sum = answer + pi;
            globalThis.__g = greeting + (flag ? '!' : '?');
            globalThis.__n = nothing;
        ");

        Assert.Equal(45.14, Convert.ToDouble(engine.GetGlobal("__sum")), 2);
        Assert.Equal("hello!", engine.GetGlobal("__g"));
        Assert.Null(engine.GetGlobal("__n"));
    }

    [Fact]
    public void NativeModule_FunctionExport_Works()
    {
        using var engine = new QuickJSEngine();
        engine.Modules.RegisterNative("qjs:math", b => b
            .ExportFunc("add", (Func<object?[], object?>)(args =>
                Convert.ToDouble(args[0]) + Convert.ToDouble(args[1])), 2));

        engine.ExecuteModule("import { add } from 'qjs:math'; globalThis.__r = add(7, 8);");

        Assert.Equal(15.0, Convert.ToDouble(engine.GetGlobal("__r")));
    }

    [Fact]
    public void NativeModule_DefaultExport_Works()
    {
        using var engine = new QuickJSEngine();
        engine.Modules.RegisterNative("qjs:def", b => b.ExportDefault("the-default"));

        engine.ExecuteModule("import d from 'qjs:def'; globalThis.__r = d;");

        Assert.Equal("the-default", engine.GetGlobal("__r"));
    }

    [Fact]
    public void NativeModule_DefaultFunc_Works()
    {
        using var engine = new QuickJSEngine();
        engine.Modules.RegisterNative("qjs:greet", b => b
            .ExportDefaultFunc((Func<object?[], object?>)(args => "hi " + args[0]), 1));

        engine.ExecuteModule("import g from 'qjs:greet'; globalThis.__r = g('bob');");

        Assert.Equal("hi bob", engine.GetGlobal("__r"));
    }

    [Fact]
    public void NativeModule_FactoryExport_IsLazy()
    {
        using var engine = new QuickJSEngine();
        int calls = 0;
        engine.Modules.RegisterNative("qjs:lazy", b => b
            .ExportFactory("count", _ => { calls++; return calls; }));

        // Module not yet imported.
        Assert.Equal(0, calls);

        engine.ExecuteModule("import { count } from 'qjs:lazy'; globalThis.__r = count;");
        Assert.Equal(1, calls);
        Assert.Equal(1, engine.GetGlobal("__r"));
    }

    [Fact]
    public void NativeModule_MixesWithSourceModule()
    {
        using var engine = new QuickJSEngine();
        engine.Modules.RegisterNative("qjs:util", b => b
            .ExportFunc("triple", (Func<object?[], object?>)(args =>
                Convert.ToInt32(args[0]) * 3), 1));
        engine.Modules.Register("user",
            "import { triple } from 'qjs:util'; export const result = triple(7);");

        engine.ExecuteModule("import { result } from 'user'; globalThis.__r = result;");

        Assert.Equal(21, engine.GetGlobal("__r"));
    }
}
