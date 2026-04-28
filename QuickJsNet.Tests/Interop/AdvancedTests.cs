using QuickJsNet.Tests.Interop.Fixtures;

namespace QuickJsNet.Tests.Interop;

public class IndexerTests
{
    [Fact]
    public void Indexer_Get_Set()
    {
        using var engine = new QuickJSEngine();
        var bag = new StringBag();
        bag.Push("a"); bag.Push("b"); bag.Push("c");
        engine.SetGlobal("bag", bag);

        var v = engine.Eval("bag[1]");
        Assert.Equal("b", v);

        engine.Execute("bag[0] = 'X';");
        Assert.Equal("X", bag[0]);
    }

    [Fact]
    public void Indexer_Count_Property()
    {
        using var engine = new QuickJSEngine();
        var bag = new StringBag();
        bag.Push("a"); bag.Push("b");
        engine.SetGlobal("bag", bag);
        var c = engine.Eval("bag.count");
        Assert.Equal(2, Convert.ToInt32(c));
    }
}

public class EventTests
{
    [Fact]
    public void On_FiresJsCallback()
    {
        using var engine = new QuickJSEngine();
        var n = new Notifier();
        engine.SetGlobal("n", n);

        engine.Execute("globalThis.last = ''; n.on('changed', s => { globalThis.last = s; });");
        n.Fire("hello");
        var last = engine.Eval("globalThis.last");
        Assert.Equal("hello", last);
    }

    [Fact]
    public void Off_StopsCallback()
    {
        using var engine = new QuickJSEngine();
        var n = new Notifier();
        engine.SetGlobal("n", n);

        engine.Execute(@"
            globalThis.calls = 0;
            globalThis.fn = () => { globalThis.calls++; };
            n.on('changed', globalThis.fn);
        ");
        n.Fire("a");
        n.Fire("b");
        engine.Execute("n.off('changed', globalThis.fn);");
        n.Fire("c");

        var calls = engine.Eval("globalThis.calls");
        Assert.Equal(2, Convert.ToInt32(calls));
    }
}

public class StaticTests
{
    [Fact]
    public void StaticMethod_ReadsAndComputes()
    {
        using var engine = new QuickJSEngine();
        engine.SetGlobalStatic<StaticMath>("StaticMath");

        var sq = engine.Eval("StaticMath.square(7)");
        Assert.Equal(49, Convert.ToInt32(sq));
        var c = engine.Eval("StaticMath.cubed");
        Assert.Equal(8, Convert.ToInt32(c));
    }

    [Fact]
    public void StaticProperty_SetReachesManaged()
    {
        using var engine = new QuickJSEngine();
        engine.SetGlobalStatic<StaticMath>("M");
        engine.Execute("M.cubed = 27;");
        Assert.Equal(27, StaticMath.Cubed);
    }
}

public class AsyncTests
{
    [Fact]
    public void TaskOfT_ReturnsPromise()
    {
        using var engine = new QuickJSEngine();
        var w = new AsyncWork();
        engine.SetGlobal("w", w);

        var r = engine.Execute("await w.computeAsync(21)");
        Assert.Equal(42, Convert.ToInt32(r));
    }

    [Fact]
    public void Task_NonGeneric_ReturnsPromise()
    {
        using var engine = new QuickJSEngine();
        var w = new AsyncWork();
        engine.SetGlobal("w", w);

        var r = engine.Execute("await w.delayAsync(); 99");
        Assert.Equal(99, Convert.ToInt32(r));
    }
}
