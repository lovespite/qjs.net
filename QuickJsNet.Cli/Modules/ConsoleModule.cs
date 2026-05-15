using System;
using System.Collections.Generic;
using QuickJsNet;
using QuickJsNet.Core;

namespace QuickJsNet.Cli.Modules;

internal static class ConsoleModule
{
    public static void Install(QuickJSEngine engine)
    {
        engine.Modules.RegisterNative("qjs:console", b =>
        {
            // readLine()
            b.ExportFunc("readLine", (object?[] args) => Console.ReadLine());

            // readKey(intercept)
            b.ExportFunc("readKey", (object?[] args) =>
            {
                bool intercept = args.Length > 0 && args[0] is bool b ? b : false;
                var info = Console.ReadKey(intercept);
                return new Dictionary<string, object>
                {
                    ["keyChar"] = info.KeyChar.ToString(),
                    ["key"] = info.Key.ToString(),
                    ["modifiers"] = info.Modifiers.ToString()
                };
            }, 1);

            // clear()
            b.ExportFunc("clear", new Func<object?[], object?>(args =>
            {
                Console.Clear();
                return null;
            }));
            
            // write(msg)
            b.ExportFunc("write", new Func<object?[], object?>(args =>
            {
                if (args.Length > 0) Console.Write(args[0]);
                return null;
            }), 1);
        });
    }
}
