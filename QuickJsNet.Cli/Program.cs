using QuickJsNet;
using QuickJsNet.Core;
using System;
using System.IO;

if (args.Length == 0)
{
    Console.WriteLine("Usage: qjs <file.js>");
    return 1;
}

var filePath = args[0];
if (!File.Exists(filePath))
{
    Console.Error.WriteLine($"Error: File not found: {filePath}");
    return 1;
}

try
{
    var code = File.ReadAllText(filePath);
    var options = new QuickJSEngineOptions
    {
        BuiltinAsModule = true,
        Fetch = true,
        FileSystem = true,
        AsyncFileSystem = true,
        Encoder = true,
        WindowsDialogs = false // Keep it CLI friendly
    };

    using var engine = new QuickJSEngine(options);
    QuickJsNet.Cli.Modules.ConsoleModule.Install(engine);
    
    // Setup module loading from current directory
    engine.Modules.BasePath = Path.GetDirectoryName(Path.GetFullPath(filePath));
    
    // Redirect console.log if needed, or just let it use default (which prints to stdout in QuickJS.NET)
    // Actually QuickJS.NET's default RegisterGlobalFunction for console.log prints to Console.Out.

    if (filePath.EndsWith(".mjs") || filePath.EndsWith(".js"))
    {
        // For benchmarks, ExecuteModule is often better as it supports top-level await
        engine.ExecuteModule(code, filePath);
    }
    else
    {
        engine.Execute(code, filePath);
    }
    
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}
