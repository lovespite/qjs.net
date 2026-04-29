using QuickJsNet.Tests.Interop.Fixtures;

namespace QuickJsNet.Tests.Interop;

public class ContainerProxyTests
{
    [Fact]
    public void Returns_StringArray_AsJsArray()
    {
        using var engine = new QuickJSEngine();
        engine.SetGlobal("c", new ContainerBag());
        var len = engine.Eval("c.names().length");
        var first = engine.Eval("c.names()[0]");
        var isArr = engine.Eval("Array.isArray(c.names())");
        Assert.Equal(3, Convert.ToInt32(len));
        Assert.Equal("a", first);
        Assert.True(Convert.ToBoolean(isArr));
    }

    [Fact]
    public void Returns_IntList_AsJsArray()
    {
        using var engine = new QuickJSEngine();
        engine.SetGlobal("c", new ContainerBag());
        var sum = engine.Eval("c.numbers().reduce((a,b)=>a+b,0)");
        Assert.Equal(10, Convert.ToInt32(sum));
    }

    [Fact]
    public void Returns_StringDict_AsJsObject()
    {
        using var engine = new QuickJSEngine();
        engine.SetGlobal("c", new ContainerBag());
        var v = engine.Eval("c.headers()['x-key']");
        var keys = engine.Eval("Object.keys(c.headers()).length");
        Assert.Equal("v1", v);
        Assert.Equal(2, Convert.ToInt32(keys));
    }

    [Fact]
    public void Accepts_JsArray_AsList()
    {
        using var engine = new QuickJSEngine();
        engine.SetGlobal("c", new ContainerBag());
        var sum = engine.Eval("c.sumOfList([1,2,3,4,5])");
        Assert.Equal(15, Convert.ToInt32(sum));
    }

    [Fact]
    public void Accepts_JsArray_AsStringArray()
    {
        using var engine = new QuickJSEngine();
        engine.SetGlobal("c", new ContainerBag());
        var s = engine.Eval("c.concat(['x','y','z'])");
        Assert.Equal("x,y,z", s);
    }
}
