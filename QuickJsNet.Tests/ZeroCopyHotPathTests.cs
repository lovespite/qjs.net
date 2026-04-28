using QuickJsNet;
using QuickJsNet.Core;
using System.Text;

namespace QuickJsNet.Tests;

/// <summary>
/// Tests that exercise the zero-copy migrated hot paths through the public
/// engine façade. These ensure behavioural equivalence with the legacy
/// copy-based implementations.
/// </summary>
public class ZeroCopyHotPathTests : IDisposable
{
    private readonly QuickJSEngine _engine = new();
    public void Dispose() => _engine.Dispose();

    // ---------- string in path (Utf8StringHelper + QJS_EvalPtr) ----------

    [Fact]
    public void Eval_AsciiString_RoundTrips()
    {
        var r = _engine.Eval("'hello world'");
        Assert.Equal("hello world", r);
    }

    [Fact]
    public void Eval_NonAsciiString_RoundTrips()
    {
        // Multi-byte UTF-8 characters: Chinese, emoji, accents
        var r = _engine.Eval("'\u4f60\u597d, \u4e16\u754c! \ud83d\ude80 caf\u00e9'");
        Assert.Equal("你好, 世界! \ud83d\ude80 café", r);
    }

    [Fact]
    public void Eval_LargeScript_ExceedsStackThreshold()
    {
        // Force the ArrayPool fallback path in Utf8StringHelper (>256B).
        var sb = new StringBuilder("var x = 0; ");
        for (int i = 0; i < 200; i++) sb.Append("x = x + 1; ");
        sb.Append("x");
        var r = _engine.Eval(sb.ToString());
        Assert.Equal(200, r);
    }

    [Fact]
    public void Eval_NonAsciiFilename_DoesNotCrash()
    {
        var r = _engine.Eval("42", filename: "脚本.js");
        Assert.Equal(42, r);
    }

    // ---------- argv out path (CallFast / stackalloc) ----------

    [Fact]
    public void Function_ZeroArgs_Works()
    {
        _engine.Eval("function f0() { return 'ok'; }");
        var r = _engine.Eval("f0()");
        Assert.Equal("ok", r);
    }

    [Fact]
    public void Function_EightArgs_Works()
    {
        _engine.Eval("function s8(a,b,c,d,e,f,g,h){return a+b+c+d+e+f+g+h;}");
        var r = _engine.Eval("s8(1,2,3,4,5,6,7,8)");
        Assert.Equal(36, r);
    }

    [Fact]
    public void Function_ManyArgs_ExceedsStackThreshold()
    {
        // 32 args > StackArgvThreshold (16) → forces ArrayPool path.
        var args = string.Join(",", Enumerable.Range(1, 32));
        _engine.Eval("function many(){let s=0;for(const v of arguments)s+=v;return s;}");
        var r = _engine.Eval($"many({args})");
        Assert.Equal(32 * 33 / 2, r);
    }

    // ---------- argv in path (ReadOnlySpan<JSValue> over native argv) ----------

    [Fact]
    public void RegisterFunction_ReceivesAllArgs()
    {
        int sum = 0;
        _engine.RegisterFunction("acc", args =>
        {
            foreach (var a in args) sum += Convert.ToInt32(a);
            return null;
        });
        _engine.Eval("acc(1,2,3,4,5)");
        Assert.Equal(15, sum);
    }

    [Fact]
    public void RegisterFunction_NoArgs_Works()
    {
        bool called = false;
        _engine.RegisterFunction("ping", () => { called = true; });
        _engine.Eval("ping()");
        Assert.True(called);
    }

    [Fact]
    public void RegisterFunction_ManyArgs_AllReceived()
    {
        int count = 0;
        _engine.RegisterFunction("cnt", args => { count = args.Length; return null; });
        _engine.Eval("cnt(0,1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20)");
        Assert.Equal(21, count);
    }

    // ---------- exception path (Utf8StringHelper + QJS_ThrowInternalErrorPtr) ----------

    [Fact]
    public void RegisterFunction_ThrowingHandler_PropagatesAsJsError()
    {
        _engine.RegisterFunction("boom", _ => throw new InvalidOperationException("kaboom-中文"));
        var ex = Assert.Throws<QuickJSException>(() => _engine.Eval("boom()"));
        Assert.Contains("kaboom-中文", ex.Message);
    }

    // ---------- repeated calls — pin/unpin & GCHandle stress ----------

    [Fact]
    public void RepeatedEvals_DoNotLeakOrCrash()
    {
        for (int i = 0; i < 500; i++)
        {
            var r = _engine.Eval($"({i} * 2)");
            Assert.Equal(i * 2, r);
        }
    }
}
