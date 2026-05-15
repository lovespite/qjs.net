using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Linq;

namespace QuickJsNet.Benchmarks;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("# Performance Benchmarks");
        Console.WriteLine();

        var scriptDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../Scripts"));
        if (!Directory.Exists(scriptDir))
        {
            scriptDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Scripts");
        }

        var cliPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../QuickJsNet.Cli/bin/Release/net10.0/QuickJsNet.Cli.exe"));
        if (!File.Exists(cliPath))
        {
             cliPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "QuickJsNet.Cli.exe");
        }

        var engines = new List<(string Name, string Command, string Args)>
        {
            ("QuickJS.NET", cliPath, "{0}"),
            ("Node.js", "node", "{0}"),
            ("Bun", "bun", "{0}")
        };

        var scripts = Directory.GetFiles(scriptDir, "*.js");

        Console.WriteLine("| Benchmark | QuickJS.NET (ms) | Node.js (ms) | Bun (ms) |");
        Console.WriteLine("| :--- | :--- | :--- | :--- |");

        foreach (var script in scripts)
        {
            var results = new List<double>();
            foreach (var engine in engines)
            {
                var time = MeasureTime(engine.Command, string.Format(engine.Args, script));
                results.Add(time);
            }

            Console.WriteLine($"| {Path.GetFileName(script)} | {results[0]:F2} | {results[1]:F2} | {results[2]:F2} |");
        }
    }

    static double MeasureTime(string command, string args)
    {
        // Warmup
        RunProcess(command, args);

        const int iterations = 5;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            RunProcess(command, args);
        }
        sw.Stop();

        return sw.Elapsed.TotalMilliseconds / iterations;
    }

    static void RunProcess(string command, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        process?.WaitForExit();
    }
}
