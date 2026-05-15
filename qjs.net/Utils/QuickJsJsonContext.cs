using System.Collections.Generic;
using System.Text.Json.Serialization;
using QuickJsNet.Modules;

namespace QuickJsNet.Utils;

[JsonSerializable(typeof(string[]))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(WindowsSimple.SelectOption[]))]
internal partial class QuickJsJsonContext : JsonSerializerContext
{
}
