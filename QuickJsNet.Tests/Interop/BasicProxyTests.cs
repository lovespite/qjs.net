using QuickJsNet.Tests.Interop.Fixtures;

// Force the binder module-init registration for [JSExport] types in this assembly.
// (ModuleInitializer fires lazily on first reference to the binder type; we
// touch a static container to trigger generation paths.)

namespace QuickJsNet.Tests.Interop;

public class BasicProxyTests
{
    [Fact]
    public void SetGlobal_Property_RoundTrip()
    {
        using var engine = new QuickJSEngine();
        var p = new Person { Name = "Alice", Age = 30 };
        engine.SetGlobal("p", p);

        var name = engine.Eval("p.name");
        var age = engine.Eval("p.age");

        Assert.Equal("Alice", name);
        Assert.Equal(30, Convert.ToInt32(age));
    }

    [Fact]
    public void Property_SetFromJs_ReachesManaged()
    {
        using var engine = new QuickJSEngine();
        var p = new Person { Name = "Alice", Age = 30 };
        engine.SetGlobal("p", p);

        engine.Execute("p.age = 42; p.name = 'Bob';");

        Assert.Equal(42, p.Age);
        Assert.Equal("Bob", p.Name);
    }

    [Fact]
    public void Method_StringAndInt_Args()
    {
        using var engine = new QuickJSEngine();
        var p = new Person { Name = "Alice" };
        engine.SetGlobal("p", p);

        var greet = engine.Eval("p.greet('World')");
        var sum = engine.Eval("p.add(2, 3)");

        Assert.Equal("Hello World from Alice", greet);
        Assert.Equal(5, Convert.ToInt32(sum));
    }

    [Fact]
    public void VoidMethod_Works()
    {
        using var engine = new QuickJSEngine();
        var p = new Person { Name = "X", Age = 1 };
        engine.SetGlobal("p", p);
        engine.Execute("p.reset();");
        Assert.Equal("", p.Name);
        Assert.Equal(0, p.Age);
    }

    [Fact]
    public void JsIgnore_NotExposed()
    {
        using var engine = new QuickJSEngine();
        var p = new Person { Name = "A", Age = 1 };
        engine.SetGlobal("p", p);
        var v = engine.Eval("typeof p.secret");
        Assert.Equal("undefined", v);
    }

    [Fact]
    public void Counter_StateAcrossCalls()
    {
        using var engine = new QuickJSEngine();
        var c = new Counter();
        engine.SetGlobal("c", c);
        engine.Execute("c.increment(); c.increment(); c.add(5);");
        var v = engine.Eval("c.value");
        Assert.Equal(7, Convert.ToInt32(v));
        Assert.Equal(7, c.Value);
    }
}
