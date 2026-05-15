using System.Text.Json.Serialization;

namespace QuickJsNet.Tests;

[JsonSerializable(typeof(string))]
internal partial class TestJsonContext : JsonSerializerContext
{
}
