using QuickJsNet.Core;
using QuickJSNet.Bindings;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;

namespace QuickJsNet.Modules;

/// <summary>
/// Configuration for the fetch module.
/// </summary>
public class FetchModuleOptions
{
    /// <summary>Proxy URL (e.g., "http://127.0.0.1:7890" or "socks5://127.0.0.1:1080")</summary>
    public string? ProxyUrl { get; set; }

    /// <summary>Skip SSL certificate validation</summary>
    public bool IgnoreSslErrors { get; set; }

    /// <summary>Default timeout in milliseconds (default: 30000)</summary>
    public int DefaultTimeoutMs { get; set; } = 30000;
}

/// <summary>
/// Registers a `fetch` function into the QuickJS engine.
/// <br />
/// JS API:<br />
///   fetch(url: string, options?: {<br />
///     method?: string,       // GET, POST, PUT, DELETE, PATCH, HEAD<br />
///     headers?: object,      // { "Content-Type": "application/json" }<br />
///     body?: string,         // Request body<br />
///     timeout?: number,      // Timeout in ms<br />
///     proxy?: string,        // Override proxy URL for this request<br />
///     ignoreSslErrors?: boolean // Override SSL setting for this request<br />
///   }): Promise&lt;{<br />
///     ok: boolean,<br />
///     status: number,<br />
///     statusText: string,<br />
///     headers: object,<br />
///     text: () => Promise<string>,<br />
///     json: () => Promise<any>,<br />
///     arrayBuffer: () => Promise<ArrayBuffer>,<br />
///     url: string<br />
///   }&gt;
/// </summary>
internal static class FetchModule
{
    private static readonly ConcurrentDictionary<long, HttpResponseMessage> _responses = [];

    public static void Install(QuickJSRuntime engine, FetchModuleOptions? options = null)
    {
        options ??= new FetchModuleOptions();

        // Register __fetch_impl(url, opts, resolve, reject)
        // Options are parsed synchronously on the JS thread;
        // the actual HTTP call runs on a ThreadPool thread.
        engine.RegisterGlobalFunction("__fetch_impl", args =>
        {
            if (args.Length < 3) return null;

            string? url = engine.GetString(args[0]);
            var resolve = engine.DupValue(args[2]);
            var reject = engine.DupValue(args[3]);

            if (string.IsNullOrEmpty(url))
            {
                engine.RejectPromise(resolve, reject, "url required");
                return null;
            }

            // Parse options from second argument (synchronous, on JS thread)
            string method = "GET";
            byte[]? body = null;
            int timeoutMs = options.DefaultTimeoutMs;
            string? proxyUrl = options.ProxyUrl;
            bool ignoreSsl = options.IgnoreSslErrors;
            string? headersJson = null;

            if (args.Length > 1 && args[1].IsObject)
            {
                var opts = args[1];
                var ctx = engine.Context;

                var mVal = QuickJSNative.QJS_GetPropertyStr(ctx, opts, "method");
                if (mVal.IsString) method = engine.GetString(mVal) ?? "GET";
                QuickJSNative.QJS_FreeValue(ctx, mVal);

                var bVal = QuickJSNative.QJS_GetPropertyStr(ctx, opts, "body");
                if (bVal.IsString) body = engine.GetStringBytesUTF8(bVal);
                else if (engine.GetByteArray(bVal) is byte[] bArr) body = bArr;
                else if (!bVal.IsNullOrUndefined)
                {
                    // If body is an object, try to JSON.stringify it
                    var bJson = QuickJSNative.QJS_JSONStringify(ctx, bVal);
                    if (bJson.IsString) body = engine.GetStringBytesUTF8(bJson);

                    QuickJSNative.QJS_FreeValue(ctx, bJson);
                }
#if DEBUG
                if (body != null) Debug.WriteLine(Encoding.UTF8.GetString(body));
#endif

                QuickJSNative.QJS_FreeValue(ctx, bVal);

                var tVal = QuickJSNative.QJS_GetPropertyStr(ctx, opts, "timeout");
                if (tVal.IsNumber) timeoutMs = engine.GetInt32(tVal);
                QuickJSNative.QJS_FreeValue(ctx, tVal);

                var pVal = QuickJSNative.QJS_GetPropertyStr(ctx, opts, "proxy");
                if (pVal.IsString) proxyUrl = engine.GetString(pVal);
                QuickJSNative.QJS_FreeValue(ctx, pVal);

                var sVal = QuickJSNative.QJS_GetPropertyStr(ctx, opts, "ignoreSslErrors");
                if (sVal.IsBool) ignoreSsl = QuickJSNative.QJS_ToBool(ctx, sVal) != 0;
                QuickJSNative.QJS_FreeValue(ctx, sVal);

                // Stringify headers to pass to the HTTP layer
                var hVal = QuickJSNative.QJS_GetPropertyStr(ctx, opts, "headers");
                if (hVal.IsObject)
                {
                    var hJson = QuickJSNative.QJS_JSONStringify(ctx, hVal);
                    if (hJson.IsString) headersJson = engine.GetString(hJson);
                    QuickJSNative.QJS_FreeValue(ctx, hJson);
                }
                QuickJSNative.QJS_FreeValue(ctx, hVal);
            }

            // Capture all parsed values and dispatch to background thread
            var capturedMethod = method;
            var capturedBody = body;
            var capturedHeadersJson = headersJson;
            var capturedProxyUrl = proxyUrl;
            var capturedIgnoreSsl = ignoreSsl;
            var capturedTimeoutMs = timeoutMs;
            var capturedUrl = url;

            engine.Promise(resolve, reject, () =>
            {
                var client = GetClient(capturedProxyUrl, capturedIgnoreSsl, capturedTimeoutMs);
                return DoFetch(capturedUrl, capturedMethod, capturedBody, capturedHeadersJson, client);
            });

            return null;
        }, 4);

        engine.RegisterGlobalFunction("__fetch_read_text", args =>
        {
            if (args.Length < 3) return null;
            var id = engine.GetInt32(args[0]);
            var resolve = engine.DupValue(args[1]);
            var reject = engine.DupValue(args[2]);

            engine.Promise(resolve, reject, () =>
            {
                if (_responses.TryRemove(id, out var response))
                {
                    using (response)
                    {
                        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    }
                }
                else
                {
                    throw new Exception("Response already read or not found");
                }
            });
            return null;
        }, 3);

        engine.RegisterGlobalFunction("__fetch_read_arrayBuffer", args =>
        {
            if (args.Length < 3) return null;
            var id = engine.GetInt32(args[0]);
            var resolve = engine.DupValue(args[1]);
            var reject = engine.DupValue(args[2]);

            engine.Promise(resolve, reject, () =>
            {
                if (_responses.TryRemove(id, out var response))
                {
                    using (response)
                    {
                        return response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    }
                }
                else
                {
                    throw new Exception("Response already read or not found");
                }
            });
            return null;
        }, 3);

        // Install the JS wrapper that provides Promise-based fetch API
        engine.Eval(@"
globalThis.fetch = function(url, options) {
    return new Promise(function(resolve, reject) {
        if (options && ArrayBuffer.isView(options.body)) {
            const view = options.body;
            let buffer = view;
            if (view.buffer) {
                if (view.byteOffset !== 0 || view.byteLength !== view.buffer.byteLength) {
                    buffer = view.buffer.slice(view.byteOffset, view.byteOffset + view.byteLength);
                } else {
                    buffer = view.buffer;
                }
            }
            options.body = buffer;
        }
        __fetch_impl(url, options || {}, function(resultJson) {
            try {
                const result = JSON.parse(resultJson);
                if (result.error) {
                    reject(new Error(result.error));
                } else {
                    result.text = function() {
                        return new Promise(function(res, rej) {
                            __fetch_read_text(result.id, res, rej);
                        });
                    };
                    result.json = function() {
                        return result.text().then(function(t) { return JSON.parse(t); });
                    };
                    result.arrayBuffer = function() {
                        return new Promise(function(res, rej) {
                            __fetch_read_arrayBuffer(result.id, res, rej);
                        });
                    };
                    resolve(result);
                }
            } catch (e) {
                reject(e);
            }
        }, reject);
    });
};
", "<fetch-init>");
    }

    private static readonly Lock _clientPoolLock = new();
    private static readonly ConcurrentDictionary<string, HttpClient> _clientPool = [];
    private static HttpClient GetClient(string? proxy, bool ignoreSslError, int timeoutMs)
    {
        string key = GetClientKey(proxy, ignoreSslError, timeoutMs);

        if (_clientPool.TryGetValue(key, out var existing)) return existing;

        lock (_clientPoolLock)
        {
            // Double-check after acquiring lock
            if (_clientPool.TryGetValue(key, out existing)) return existing;

            var handler = new HttpClientHandler();

            if (!string.IsNullOrEmpty(proxy))
            {
                handler.Proxy = new WebProxy(proxy);
                handler.UseProxy = true;
            }

            if (ignoreSslError)
            {
                handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            }

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMilliseconds(timeoutMs)
            };
            client.DefaultRequestHeaders.UserAgent.TryParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/115.0.0.0 Safari/537.36 QJS-f/1.0");
            _clientPool[key] = client;
#if DEBUG
            Debug.WriteLine($"Created new HttpClient for key: {key}");
#endif
            return client;
        }
    }
    private static string GetClientKey(string? proxy, bool ignoreSslError, int timeoutMs) => $"{proxy ?? "direct"}|{ignoreSslError}|{timeoutMs}";
    private static long _responseCounter = 0;

    private static string DoFetch(string url, string method, byte[]? body, string? headersJson, HttpClient client)
    {
        // Validate URL to prevent SSRF - only allow http(s) schemes
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            return "{\"error\":\"Invalid URL: only http and https schemes are allowed\"}";
        }
        var request = new HttpRequestMessage(new HttpMethod(method), uri);
        Dictionary<string, string>? headerDict = null;
        // Parse and set request headers
        if (!string.IsNullOrEmpty(headersJson))
        {
            try
            {
                // Simple JSON object parsing for headers
                headerDict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(headersJson);
                if (headerDict != null)
                {
                    foreach (var kvp in headerDict)
                    {
                        // Try to add as request header first, then as content header
                        if (!request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value))
                        {
                            // Will be set on content if applicable
#if DEBUG
                            Debug.WriteLine($"WRN! Cannot add Http request header `{kvp.Key}: {kvp.Value}`");
#endif
                        }
                    }
                }
            }
            catch { /* ignore header parse errors */ }
        }

        if (body != null)
        {
            request.Content = new ByteArrayContent(body);
            if (headerDict != null) foreach (var kvp in headerDict) request.Content.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
        }

        // Execute synchronously (we're called from JS sync context)
        var response = client.SendAsync(request).GetAwaiter().GetResult();
        var id = Interlocked.Increment(ref _responseCounter);
        _responses[id] = response;

        // Build response headers JSON
        var respHeaders = new StringBuilder("{");
        bool first = true;
        foreach (var header in response.Headers)
        {
            if (!first) respHeaders.Append(',');
            first = false;
            respHeaders.Append('"');
            respHeaders.Append(EscapeJsonString(header.Key));
            respHeaders.Append("\":\"");
            respHeaders.Append(EscapeJsonString(string.Join(", ", header.Value)));
            respHeaders.Append('"');
        }
        foreach (var header in response.Content.Headers)
        {
            if (!first) respHeaders.Append(',');
            first = false;
            respHeaders.Append('"');
            respHeaders.Append(EscapeJsonString(header.Key));
            respHeaders.Append("\":\"");
            respHeaders.Append(EscapeJsonString(string.Join(", ", header.Value)));
            respHeaders.Append('"');
        }
        respHeaders.Append('}');

        int statusCode = (int)response.StatusCode;
        string statusText = EscapeJsonString(response.ReasonPhrase ?? "");
        string escapedUrl = EscapeJsonString(url);

        return $"{{\"ok\":{(response.IsSuccessStatusCode ? "true" : "false")}," +
               $"\"status\":{statusCode}," +
               $"\"statusText\":\"{statusText}\"," +
               $"\"headers\":{respHeaders}," +
               $"\"id\":{id}," +
               $"\"url\":\"{escapedUrl}\"}}";
    }

    ///// <summary>
    ///// Immediately reject a fetch call (used for early validation errors).
    ///// </summary>
    //private static void RejectWith(QuickJSEngine engine, JSValue resolve, JSValue reject, string errorMessage)
    //{
    //    var eventLoop = engine.GetLooper();
    //    eventLoop.TrackAsyncOp();
    //    eventLoop.Post(() =>
    //    {
    //        var err = engine.ManagedToJSValue(errorMessage);
    //        var undef = QuickJSNative.QJS_NewUndefined();
    //        var r = engine.Call(reject, undef, err);
    //        engine.FreeValue(r);
    //        engine.FreeValue(err);
    //        engine.FreeValue(resolve);
    //        engine.FreeValue(reject);
    //    });
    //}

    private static string EscapeJsonString(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20)
                        sb.Append($"\\u{(int)c:X4}");
                    else
                        sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}