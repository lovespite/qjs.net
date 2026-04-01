using QuickJsNet;
using QuickJsNet.Core;
using QuickJsNet.Modules;

namespace QuickJsNet.Tests;

/// <summary>
/// Tests for the public <see cref="QuickJSEngine"/> facade.
/// Each test creates its own engine instance to ensure isolation.
/// </summary>
public class QuickJSEngineTests : IDisposable
{
    private readonly QuickJSEngine _engine;

    public QuickJSEngineTests()
    {
        _engine = new QuickJSEngine();
    }

    public void Dispose() => _engine.Dispose();

    // ════════════════════ Construction ════════════════════

    [Fact]
    public void DefaultConstructor_CreatesWorkingEngine()
    {
        using var engine = new QuickJSEngine();
        var result = engine.Eval("1 + 1");
        Assert.Equal(2, result);
    }

    [Fact]
    public void OptionsConstructor_CreatesWorkingEngine()
    {
        using var engine = new QuickJSEngine(new QuickJSEngineOptions
        {
            MemoryLimit = 64 * 1024 * 1024
        });
        var result = engine.Eval("'hello'");
        Assert.Equal("hello", result);
    }

    [Fact]
    public void DelegateConstructor_CreatesWorkingEngine()
    {
        using var engine = new QuickJSEngine(opt =>
        {
            opt.MemoryLimit = 32 * 1024 * 1024;
        });
        var result = engine.Eval("42");
        Assert.Equal(42, result);
    }

    [Fact]
    public void Constructor_NullOptions_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new QuickJSEngine((QuickJSEngineOptions)null!));
    }

    // ════════════════════ Eval – Value Types ════════════════════

    [Fact]
    public void Eval_IntegerExpression_ReturnsInt()
    {
        var result = _engine.Eval("2 + 3");
        Assert.Equal(5, result);
    }

    [Fact]
    public void Eval_FloatingPointExpression_ReturnsDouble()
    {
        var result = _engine.Eval("1.5 + 2.5");
        Assert.Equal(4.0, result);
    }

    [Fact]
    public void Eval_StringExpression_ReturnsString()
    {
        var result = _engine.Eval("'Hello' + ' ' + 'World'");
        Assert.Equal("Hello World", result);
    }

    [Fact]
    public void Eval_BooleanTrue_ReturnsBool()
    {
        var result = _engine.Eval("true");
        Assert.Equal(true, result);
    }

    [Fact]
    public void Eval_BooleanFalse_ReturnsBool()
    {
        var result = _engine.Eval("false");
        Assert.Equal(false, result);
    }

    [Fact]
    public void Eval_Null_ReturnsNull()
    {
        var result = _engine.Eval("null");
        Assert.Null(result);
    }

    [Fact]
    public void Eval_Undefined_ReturnsNull()
    {
        var result = _engine.Eval("undefined");
        Assert.Null(result);
    }

    [Fact]
    public void Eval_ObjectLiteral_ReturnsJsonString()
    {
        var result = _engine.Eval("({a: 1, b: 'two'})");
        Assert.IsType<string>(result);
        Assert.Contains("\"a\"", (string)result!);
        Assert.Contains("\"b\"", (string)result!);
    }

    [Fact]
    public void Eval_ArrayLiteral_ReturnsJsonString()
    {
        var result = _engine.Eval("[1, 2, 3]");
        Assert.Equal("[1,2,3]", result);
    }

    [Fact]
    public void Eval_LargeInteger_ReturnsDouble()
    {
        var result = _engine.Eval("Number.MAX_SAFE_INTEGER");
        Assert.Equal(9007199254740991.0, result);
    }

    [Fact]
    public void Eval_NegativeNumber_ReturnsCorrectValue()
    {
        Assert.Equal(-42, _engine.Eval("-42"));
    }

    // ════════════════════ Eval – Statements ════════════════════

    [Fact]
    public void Eval_VariableDeclarationAndUse()
    {
        _engine.Eval("var x = 10;");
        var result = _engine.Eval("x * 2");
        Assert.Equal(20, result);
    }

    [Fact]
    public void Eval_FunctionDeclarationAndCall()
    {
        _engine.Eval("function square(n) { return n * n; }");
        var result = _engine.Eval("square(7)");
        Assert.Equal(49, result);
    }

    [Fact]
    public void Eval_TemplateLiterals()
    {
        _engine.Eval("var name = 'QuickJS'");
        var result = _engine.Eval("`Hello, ${name}!`");
        Assert.Equal("Hello, QuickJS!", result);
    }

    [Fact]
    public void Eval_ArrowFunction()
    {
        _engine.Eval("const add = (a, b) => a + b;");
        var result = _engine.Eval("add(3, 4)");
        Assert.Equal(7, result);
    }

    [Fact]
    public void Eval_Destructuring()
    {
        _engine.Eval("const {a, b} = {a: 10, b: 20};");
        Assert.Equal(10, _engine.Eval("a"));
        Assert.Equal(20, _engine.Eval("b"));
    }

    // ════════════════════ Execute – Async Support ════════════════════

    [Fact]
    public void Execute_TopLevelAwait_ResolvesPromise()
    {
        var result = _engine.Execute("await Promise.resolve(42)");
        Assert.Equal(42, result);
    }

    [Fact]
    public void Execute_AsyncFunction_ResolvesResult()
    {
        _engine.Execute(@"
            async function fetchData() {
                return 'data-loaded';
            }
        ");
        var result = _engine.Execute("await fetchData()");
        Assert.Equal("data-loaded", result);
    }

    [Fact]
    public void Execute_SetTimeout_Completes()
    {
        var result = _engine.Execute(@"
            await new Promise(resolve => {
                setTimeout(() => resolve('done'), 10);
            })
        ");
        Assert.Equal("done", result);
    }

    [Fact]
    public void Execute_PromiseChain_ResolvesCorrectly()
    {
        var result = _engine.Execute(@"
            await Promise.resolve(1)
                .then(v => v + 1)
                .then(v => v * 3)
        ");
        Assert.Equal(6, result);
    }

    [Fact]
    public void Execute_PromiseAll_ResolvesArray()
    {
        var result = _engine.Execute(@"
            await Promise.all([
                Promise.resolve(1),
                Promise.resolve(2),
                Promise.resolve(3)
            ])
        ");
        Assert.Equal("[1,2,3]", result);
    }

    // ════════════════════ Error Handling ════════════════════

    [Fact]
    public void Eval_SyntaxError_ThrowsQuickJSException()
    {
        var ex = Assert.Throws<QuickJSException>(() => _engine.Eval("function("));
        Assert.NotEmpty(ex.Message);
    }

    [Fact]
    public void Eval_ReferenceError_ThrowsQuickJSException()
    {
        var ex = Assert.Throws<QuickJSException>(() => _engine.Eval("nonExistentVar.foo"));
        Assert.Contains("not defined", ex.Message);
    }

    [Fact]
    public void Eval_TypeError_ThrowsQuickJSException()
    {
        var ex = Assert.Throws<QuickJSException>(() => _engine.Eval("null.property"));
        Assert.NotEmpty(ex.Message);
    }

    [Fact]
    public void Eval_ExplicitThrow_ThrowsQuickJSException()
    {
        var ex = Assert.Throws<QuickJSException>(() =>
            _engine.Execute("throw new Error('custom error')"));
        Assert.Contains("custom error", ex.Message);
    }

    [Fact]
    public void Execute_RejectedPromise_ThrowsQuickJSException()
    {
        var ex = Assert.Throws<QuickJSException>(() =>
            _engine.Execute("await Promise.reject('promise failed')"));
        Assert.Contains("promise failed", ex.Message);
    }

    // ════════════════════ Global Variables ════════════════════

    [Fact]
    public void SetGlobal_String_AccessibleFromJS()
    {
        _engine.SetGlobal("greeting", "Hello");
        var result = _engine.Eval("greeting");
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void SetGlobal_Int_AccessibleFromJS()
    {
        _engine.SetGlobal("count", 42);
        var result = _engine.Eval("count + 8");
        Assert.Equal(50, result);
    }

    [Fact]
    public void SetGlobal_Double_AccessibleFromJS()
    {
        _engine.SetGlobal("pi", 3.14159);
        // Math.floor returns an integer-valued number; QuickJS may store as int
        var result = _engine.Eval("Math.floor(pi)");
        Assert.Equal(3, Convert.ToInt32(result));
    }

    [Fact]
    public void SetGlobal_Bool_AccessibleFromJS()
    {
        _engine.SetGlobal("flag", true);
        Assert.Equal(true, _engine.Eval("flag"));
    }

    [Fact]
    public void SetGlobal_Null_AccessibleFromJS()
    {
        _engine.SetGlobal("empty", null);
        Assert.Null(_engine.Eval("empty"));
    }

    [Fact]
    public void GetGlobal_ReadsJSVariable()
    {
        _engine.Eval("var message = 'from JS'");
        Assert.Equal("from JS", _engine.GetGlobal("message"));
    }

    [Fact]
    public void GetGlobal_Undefined_ReturnsNull()
    {
        Assert.Null(_engine.GetGlobal("not_defined_var_12345"));
    }

    [Fact]
    public void Indexer_SetAndGet()
    {
        _engine["x"] = 100;
        Assert.Equal(100, _engine["x"]);
        Assert.Equal(100, _engine.Eval("x"));
    }

    [Fact]
    public void Indexer_OverwriteValue()
    {
        _engine["val"] = "first";
        Assert.Equal("first", _engine["val"]);
        _engine["val"] = "second";
        Assert.Equal("second", _engine["val"]);
    }

    // ════════════════════ Function Registration ════════════════════

    [Fact]
    public void RegisterFunction_FuncWithReturn_CallableFromJS()
    {
        _engine.RegisterFunction("add", args =>
        {
            double a = Convert.ToDouble(args[0]);
            double b = Convert.ToDouble(args[1]);
            return a + b;
        }, argCount: 2);

        // QuickJS may optimize integer-valued doubles back to int
        var result = _engine.Eval("add(3, 4)");
        Assert.Equal(7, Convert.ToInt32(result));
    }

    [Fact]
    public void RegisterFunction_FuncReturnsString()
    {
        _engine.RegisterFunction("greet", args =>
        {
            return $"Hello, {args[0]}!";
        }, argCount: 1);

        var result = _engine.Eval("greet('World')");
        Assert.Equal("Hello, World!", result);
    }

    [Fact]
    public void RegisterFunction_FuncReturnsNull()
    {
        _engine.RegisterFunction("nothing", _ => null, argCount: 0);
        var result = _engine.Eval("nothing()");
        Assert.Null(result);
    }

    [Fact]
    public void RegisterFunction_ActionNoArgs_CallableFromJS()
    {
        var called = false;
        _engine.RegisterFunction("notify", () => { called = true; });
        _engine.Eval("notify()");
        Assert.True(called);
    }

    [Fact]
    public void RegisterFunction_ActionWithArgs_ReceivesArguments()
    {
        string? captured = null;
        _engine.RegisterFunction("capture", args =>
        {
            captured = args[0]?.ToString();
        }, argCount: 1);

        _engine.Eval("capture('test-value')");
        Assert.Equal("test-value", captured);
    }

    [Fact]
    public void RegisterFunction_MultipleRegistrations()
    {
        _engine.RegisterFunction("inc", args => Convert.ToInt32(args[0]) + 1, argCount: 1);
        _engine.RegisterFunction("dec", args => Convert.ToInt32(args[0]) - 1, argCount: 1);

        Assert.Equal(6, _engine.Eval("inc(5)"));
        Assert.Equal(4, _engine.Eval("dec(5)"));
    }

    [Fact]
    public void RegisterFunction_ExceptionInHandler_ThrowsJSError()
    {
        _engine.RegisterFunction("boom", _ =>
        {
            throw new InvalidOperationException("C# error");
        });

        var ex = Assert.Throws<QuickJSException>(() => _engine.Eval("boom()"));
        Assert.Contains("C# error", ex.Message);
    }

    [Fact]
    public void RegisterFunction_UsedInAsyncContext()
    {
        _engine.RegisterFunction("computeSync", args =>
        {
            return Convert.ToInt32(args[0]) * 2;
        }, argCount: 1);

        var result = _engine.Execute(@"
            async function run() {
                const val = computeSync(21);
                return val;
            }
            await run()
        ");
        Assert.Equal(42, result);
    }

    [Fact]
    public void RegisterFunction_WithDelegate()
    {
        static int Calc(int a, int b) => a + b;
        _engine.RegisterFunction("calc", Calc);
        var result = _engine.Eval("calc(10, 15)");
        Assert.Equal(25, result);
    }

    // ════════════════════ HasFunction / Invoke ════════════════════

    [Fact]
    public void HasFunction_ExistingFunction_ReturnsTrue()
    {
        _engine.Eval("function myFunc() { return 1; }");
        Assert.True(_engine.HasFunction("myFunc"));
    }

    [Fact]
    public void HasFunction_NonExistentFunction_ReturnsFalse()
    {
        Assert.False(_engine.HasFunction("nonExistentFunction12345"));
    }

    [Fact]
    public void HasFunction_NonFunction_ReturnsFalse()
    {
        _engine.Eval("var notAFunc = 42;");
        Assert.False(_engine.HasFunction("notAFunc"));
    }

    [Fact]
    public void Invoke_SimpleFunction_ReturnsResult()
    {
        _engine.Eval("function multiply(a, b) { return a * b; }");
        var result = _engine.Invoke("multiply", 6, 7);
        Assert.Equal(42, result);
    }

    [Fact]
    public void Invoke_FunctionWithStringArgs()
    {
        _engine.Eval("function concat(a, b) { return a + b; }");
        var result = _engine.Invoke("concat", "foo", "bar");
        Assert.Equal("foobar", result);
    }

    [Fact]
    public void Invoke_AsyncFunction_ResolvesPromise()
    {
        _engine.Execute(@"
            async function asyncDouble(n) {
                return n * 2;
            }
        ");
        var result = _engine.Invoke("asyncDouble", 21);
        Assert.Equal(42, result);
    }

    [Fact]
    public void Invoke_NoArgFunction()
    {
        _engine.Eval("function getFortyTwo() { return 42; }");
        Assert.Equal(42, _engine.Invoke("getFortyTwo"));
    }

    // ════════════════════ Built-in Modules ════════════════════

    [Fact]
    public void Module_TextEncoder_Available()
    {
        var result = _engine.Eval("typeof TextEncoder");
        Assert.Equal("function", result);
    }

    [Fact]
    public void Module_TextDecoder_Available()
    {
        var result = _engine.Eval("typeof TextDecoder");
        Assert.Equal("function", result);
    }

    [Fact]
    public void Module_TextEncoder_EncodeAndDecode()
    {
        var result = _engine.Eval(@"
            const encoder = new TextEncoder();
            const decoder = new TextDecoder();
            const encoded = encoder.encode('Hello');
            decoder.decode(encoded);
        ");
        Assert.Equal("Hello", result);
    }

    [Fact]
    public void Module_Fs_Available()
    {
        var result = _engine.Eval("typeof fs");
        Assert.Equal("object", result);
    }

    [Fact]
    public void Module_FsAsync_Available()
    {
        var result = _engine.Eval("typeof fsAsync");
        Assert.Equal("object", result);
    }

    [Fact]
    public void Module_Fetch_Available()
    {
        var result = _engine.Eval("typeof fetch");
        Assert.Equal("function", result);
    }

    [Fact]
    public void Module_SetTimeout_Available()
    {
        Assert.Equal("function", _engine.Eval("typeof setTimeout"));
        Assert.Equal("function", _engine.Eval("typeof setInterval"));
        Assert.Equal("function", _engine.Eval("typeof clearTimeout"));
        Assert.Equal("function", _engine.Eval("typeof clearInterval"));
    }

    // ════════════════════ Module Opt-out ════════════════════

    [Fact]
    public void Options_DisableEncoder_NotAvailable()
    {
        using var engine = new QuickJSEngine(opt => opt.Encoder = false);
        Assert.Equal("undefined", engine.Eval("typeof TextEncoder"));
    }

    [Fact]
    public void Options_DisableFileSystem_NotAvailable()
    {
        using var engine = new QuickJSEngine(opt => opt.FileSystem = false);
        Assert.Equal("undefined", engine.Eval("typeof fs"));
    }

    [Fact]
    public void Options_DisableAsyncFileSystem_NotAvailable()
    {
        using var engine = new QuickJSEngine(opt => opt.AsyncFileSystem = false);
        Assert.Equal("undefined", engine.Eval("typeof fsAsync"));
    }

    [Fact]
    public void Options_DisableFetch_NotAvailable()
    {
        using var engine = new QuickJSEngine(opt => opt.Fetch = false);
        Assert.Equal("undefined", engine.Eval("typeof fetch"));
    }

    [Fact]
    public void Options_MinimalEngine_OnlyCore()
    {
        using var engine = new QuickJSEngine(opt =>
        {
            opt.Encoder = false;
            opt.FileSystem = false;
            opt.AsyncFileSystem = false;
            opt.Fetch = false;
            opt.WindowsDialogs = false;
        });

        // Core JS still works
        Assert.Equal(42, engine.Eval("40 + 2"));
        // But modules are absent
        Assert.Equal("undefined", engine.Eval("typeof TextEncoder"));
        Assert.Equal("undefined", engine.Eval("typeof fetch"));
        Assert.Equal("undefined", engine.Eval("typeof fs"));
    }

    // ════════════════════ File System Module ════════════════════

    [Fact]
    public void Fs_Getcwd_ReturnsString()
    {
        var result = _engine.Eval("fs.getcwd()");
        Assert.IsType<string>(result);
        Assert.NotEmpty((string)result!);
    }

    [Fact]
    public void Fs_WriteAndReadFile()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"qjs_test_{Guid.NewGuid()}.txt");
        try
        {
            _engine.SetGlobal("__testFile", tempFile);
            _engine.Eval("fs.writeFile(__testFile, 'hello from js')");
            var content = _engine.Eval("fs.readFile(__testFile)");
            Assert.Equal("hello from js", content);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FsAsync_ReadFile_ReturnsViaPromise()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"qjs_async_test_{Guid.NewGuid()}.txt");
        try
        {
            File.WriteAllText(tempFile, "async content");
            _engine.SetGlobal("__testFile", tempFile);
            var result = _engine.Execute("await fsAsync.readFile(__testFile)");
            Assert.Equal("async content", result);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    // ════════════════════ GC ════════════════════

    [Fact]
    public void RunGC_DoesNotBreakEngine()
    {
        _engine.Eval("var arr = []; for(var i=0; i<1000; i++) arr.push({x:i});");
        _engine.RunGC();
        // Engine still works after GC
        var result = _engine.Eval("arr.length");
        Assert.Equal(1000, result);
    }

    // ════════════════════ Version ════════════════════

    [Fact]
    public void Version_ReturnsNonEmptyString()
    {
        Assert.NotNull(QuickJSEngine.Version);
        Assert.NotEmpty(QuickJSEngine.Version);
    }

    // ════════════════════ Dispose ════════════════════

    [Fact]
    public void Dispose_SubsequentCalls_ThrowObjectDisposedException()
    {
        var engine = new QuickJSEngine();
        engine.Dispose();

        Assert.Throws<ObjectDisposedException>(() => engine.Eval("1"));
        Assert.Throws<ObjectDisposedException>(() => engine.Execute("1"));
        Assert.Throws<ObjectDisposedException>(() => engine.Invoke("fn"));
        Assert.Throws<ObjectDisposedException>(() => engine.HasFunction("fn"));
        Assert.Throws<ObjectDisposedException>(() => engine.SetGlobal("x", 1));
        Assert.Throws<ObjectDisposedException>(() => engine.GetGlobal("x"));
        Assert.Throws<ObjectDisposedException>(() => _ = engine["x"]);
        Assert.Throws<ObjectDisposedException>(() => engine["x"] = 1);
        Assert.Throws<ObjectDisposedException>(() =>
            engine.RegisterFunction("f", _ => null));
        Assert.Throws<ObjectDisposedException>(() =>
            engine.RegisterFunction("f", () => { }));
        Assert.Throws<ObjectDisposedException>(() =>
            engine.RegisterFunction("f", (Action<object?[]>)(_ => { })));
        Assert.Throws<ObjectDisposedException>(() => engine.RunGC());
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var engine = new QuickJSEngine();
        engine.Dispose();
        engine.Dispose(); // should be idempotent
    }

    // ════════════════════ OnLog Event ════════════════════

    [Fact]
    public void OnLog_ConsoleLog_FiresEvent()
    {
        var logs = new List<string>();
        _engine.OnLog += (level, msg) => logs.Add(msg);

        _engine.Eval("console.log('test message')");

        Assert.NotEmpty(logs);
        Assert.Contains(logs, l => l.Contains("test message"));
    }

    // ════════════════════ Complex Scenarios ════════════════════

    [Fact]
    public void Scenario_BidirectionalInterop()
    {
        // C# → JS → C# round-trip
        var results = new List<int>();

        _engine.RegisterFunction("pushResult", args =>
        {
            results.Add(Convert.ToInt32(args[0]));
        }, argCount: 1);

        _engine.SetGlobal("baseValue", 100);

        _engine.Eval(@"
            for (let i = 0; i < 5; i++) {
                pushResult(baseValue + i);
            }
        ");

        Assert.Equal([100, 101, 102, 103, 104], results);
    }

    [Fact]
    public void Scenario_JSONProcessing()
    {
        _engine.Eval(@"
            function processJson(jsonStr) {
                const obj = JSON.parse(jsonStr);
                obj.processed = true;
                obj.count = obj.items.length;
                return JSON.stringify(obj);
            }
        ");

        var input = """{"items":["a","b","c"]}""";
        var result = _engine.Invoke("processJson", input);

        Assert.IsType<string>(result);
        var json = (string)result!;
        Assert.Contains("\"processed\":true", json);
        Assert.Contains("\"count\":3", json);
    }

    [Fact]
    public void Scenario_ErrorInPromiseChain_ThrowsWithMessage()
    {
        var ex = Assert.Throws<QuickJSException>(() =>
            _engine.Execute(@"
                await Promise.resolve(1)
                    .then(v => { throw new Error('chain error'); })
            "));
        Assert.Contains("chain error", ex.Message);
    }

    [Fact]
    public void Scenario_MultipleSetTimeouts_AllExecute()
    {
        var result = _engine.Execute(@"
            var order = [];
            await new Promise(resolve => {
                setTimeout(() => { order.push(1); }, 10);
                setTimeout(() => { order.push(2); }, 20);
                setTimeout(() => { order.push(3); resolve(); }, 30);
            });
            JSON.stringify(order)
        ");
        Assert.Equal("[1,2,3]", result);
    }

    [Fact]
    public void Scenario_SetInterval_FiresMultipleTimes()
    {
        var result = _engine.Execute(@"
            var count = 0;
            await new Promise(resolve => {
                const id = setInterval(() => {
                    count++;
                    if (count >= 3) {
                        clearInterval(id);
                        resolve(count);
                    }
                }, 10);
            })
        ");
        Assert.Equal(3, result);
    }

    [Fact]
    public void Scenario_RecursiveFunction()
    {
        _engine.Eval(@"
            function factorial(n) {
                if (n <= 1) return 1;
                return n * factorial(n - 1);
            }
        ");
        Assert.Equal(120, _engine.Invoke("factorial", 5));
    }

    [Fact]
    public void Scenario_ClosureState()
    {
        _engine.Eval(@"
            function makeCounter() {
                let count = 0;
                return function() { return ++count; };
            }
            var counter = makeCounter();
        ");
        Assert.Equal(1, _engine.Eval("counter()"));
        Assert.Equal(2, _engine.Eval("counter()"));
        Assert.Equal(3, _engine.Eval("counter()"));
    }

    [Fact]
    public void Scenario_MapFilterReduce()
    {
        var result = _engine.Eval("[1,2,3,4,5].filter(x => x % 2 !== 0).map(x => x * x).reduce((a,b) => a+b, 0)");
        Assert.Equal(35, result);
    }

    [Fact]
    public void Scenario_RegExp()
    {
        var result = _engine.Eval("'hello world 123'.match(/\\d+/)[0]");
        Assert.Equal("123", result);
    }

    [Fact]
    public void Scenario_DateNow_ReturnsNumber()
    {
        var result = _engine.Eval("typeof Date.now()");
        Assert.Equal("number", result);
    }

    [Fact]
    public void Scenario_UnicodeString()
    {
        _engine.SetGlobal("unicodeTest", "你好世界🌍");
        var result = _engine.Eval("unicodeTest");
        Assert.Equal("你好世界🌍", result);
    }
}
