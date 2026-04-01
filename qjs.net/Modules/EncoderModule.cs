using QuickJsNet.Core;
using System.Text;

namespace QuickJsNet.Modules;

internal static class EncoderModule
{
    public static void Install(QuickJSRuntime engine)
    {
        // Register TextEncoder / TextDecoder natively
        engine.RegisterGlobalFunction("__builtin_encodeText", args =>
        {
            if (args.Length < 1) return Array.Empty<byte>();
            var str = engine.GetString(args[0]) ?? "";
            return Encoding.UTF8.GetBytes(str);
        }, 1);

        engine.RegisterGlobalFunction("__builtin_decodeText", args =>
        {
            if (args.Length < 1) return "";
            var bytes = engine.GetByteArray(args[0]);
            return bytes is null ? string.Empty : Encoding.UTF8.GetString(bytes);
        }, 1);

        engine.Eval(
            """
            globalThis.TextEncoder = class TextEncoder {
                constructor() { this.encoding = 'utf-8'; }
                encode(str) { return new Uint8Array(__builtin_encodeText(str || '')); }
            };
            globalThis.TextDecoder = class TextDecoder {
                constructor(encoding = 'utf-8') { this.encoding = encoding; }
                decode(buf) {
                    if (!buf) return '';
                    let ab = buf;
                    if (buf.buffer) {
                        if (buf.byteOffset !== 0 || buf.byteLength !== buf.buffer.byteLength) {
                            ab = buf.buffer.slice(buf.byteOffset, buf.byteOffset + buf.byteLength);
                        } else {
                            ab = buf.buffer;
                        }
                    }
                    return __builtin_decodeText(ab);
                }
            };
            """, "<textencoder>");
    }
}
