using QuickJsNet.Interop;

namespace QuickJsNet.Tests.Interop.Fixtures;

[JSExport]
public partial class Person
{
    public string Name { get; set; } = "";
    public int Age { get; set; }

    public string Greet(string who) => $"Hello {who} from {Name}";

    public int Add(int a, int b) => a + b;

    public void Reset() { Name = ""; Age = 0; }

    [JSIgnore]
    public string Secret { get; set; } = "shh";
}

[JSExport]
public partial class Counter
{
    private int _value;
    public int Value => _value;
    public void Increment() => _value++;
    public void Add(int n) => _value += n;
}

[JSExport]
public partial class StringBag
{
    private readonly List<string> _items = new();
    public int Count => _items.Count;
    public void Push(string s) => _items.Add(s);
    public string this[int i]
    {
        get => _items[i];
        set => _items[i] = value;
    }
}

[JSExport]
public partial class StaticMath
{
    public static int Square(int n) => n * n;
    public static int Cubed { get; set; } = 8;
}

[JSExport]
public partial class Notifier
{
    public event Action<string>? Changed;
    public void Fire(string s) => Changed?.Invoke(s);
}

[JSExport]
public partial class AsyncWork
{
    public Task<int> ComputeAsync(int x)
        => Task.Run(() => x * 2);

    public Task DelayAsync()
        => Task.CompletedTask;
}

[JSExport]
public partial class ContainerBag
{
    public string[] Names() => new[] { "a", "b", "c" };

    public List<int> Numbers() => new() { 1, 2, 3, 4 };

    public Dictionary<string, string> Headers() => new()
    {
        ["x-key"] = "v1",
        ["x-other"] = "v2",
    };

    public Dictionary<string, object> Mixed() => new()
    {
        ["count"] = 42,
        ["name"] = "abc",
        ["ok"] = true,
    };

    public int SumOfList(List<int> values)
    {
        int s = 0;
        if (values != null) foreach (var v in values) s += v;
        return s;
    }

    public string Concat(string[] parts) => parts is null ? "" : string.Join(",", parts);
}
