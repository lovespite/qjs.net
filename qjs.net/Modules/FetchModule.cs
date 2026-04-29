using QuickJsNet.Core;
using QuickJsNet.Interop;
using QuickJSNet.Bindings;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
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
/// A fetch HTTP response wrapped as a [JSExport] proxy. Properties expose
/// status/headers/url. Methods <c>text()</c> / <c>arrayBuffer()</c> / <c>json()</c>
/// each return a Promise. Read methods consume the body once.
/// </summary>
[JSExport]
public sealed partial class FetchResponse : IDisposable
{
    private HttpResponseMessage? _response;
    private readonly Dictionary<string, string> _headers;

    public bool Ok { get; }
    public int Status { get; }
    public string StatusText { get; }
    public string Url { get; }

    public Dictionary<string, string> Headers => _headers;

    internal FetchResponse(HttpResponseMessage response, string url)
    {
        _response = response;
        Url = url;
        Ok = response.IsSuccessStatusCode;
        Status = (int)response.StatusCode;
        StatusText = response.ReasonPhrase ?? string.Empty;

        _headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in response.Headers)
            _headers[h.Key] = string.Join(", ", h.Value);
        foreach (var h in response.Content.Headers)
            _headers[h.Key] = string.Join(", ", h.Value);
    }

    /// <summary>Read the body as text. Consumes the response.</summary>
    public Task<string> Text() => Task.Run(async () =>
    {
        var resp = Interlocked.Exchange(ref _response, null)
            ?? throw new InvalidOperationException("Response already read or disposed");
        using (resp)
        {
            return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        }
    });

    /// <summary>Read the body as an ArrayBuffer (byte[]). Consumes the response.</summary>
    public Task<byte[]> ArrayBuffer() => Task.Run(async () =>
    {
        var resp = Interlocked.Exchange(ref _response, null)
            ?? throw new InvalidOperationException("Response already read or disposed");
        using (resp)
        {
            return await resp.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        }
    });

    /// <summary>
    /// Read the body as text, then JSON.parse it on the JS thread and resolve
    /// with the parsed value. Consumes the response.
    /// </summary>
    private static readonly byte[] s_jsonFilename = "<fetch.json>\0"u8.ToArray();

    public JSValue Json(QuickJSRuntime runtime)
    {
        var task = Text();
        return JSPromiseBridge.FromTaskWithJsProjection(runtime, task, (rt, txt) =>
        {
            var s = txt ?? "null";
            var bytes = Encoding.UTF8.GetBytes(s);
            unsafe
            {
                fixed (byte* p = bytes)
                fixed (byte* fp = s_jsonFilename)
                {
                    return QuickJSNative.QJS_ParseJSONPtr(rt.Context, (IntPtr)p,
                        (nuint)bytes.Length, (IntPtr)fp);
                }
            }
        });
    }

    /// <summary>
    /// Take ownership of the response body as a streaming <see cref="Stream"/>.
    /// Mutually exclusive with <see cref="Text"/> / <see cref="ArrayBuffer"/> /
    /// <see cref="Json"/> — the first reader wins; subsequent readers throw
    /// <see cref="InvalidOperationException"/>. The returned stream owns the
    /// <see cref="HttpResponseMessage"/> and disposes it when closed.
    /// </summary>
    public Stream Body()
    {
        var resp = Interlocked.Exchange(ref _response, null)
            ?? throw new InvalidOperationException("Response already read or disposed");
        var inner = resp.Content.ReadAsStream();
        return new Stream(new HttpOwningStream(resp, inner),
            readable: true, writable: false, ownsInner: true);
    }

    public void Dispose()
    {
        var resp = Interlocked.Exchange(ref _response, null);
        resp?.Dispose();
    }

    ~FetchResponse() => Dispose();
}

/// <summary>
/// A wrapper Stream that disposes the owning <see cref="HttpResponseMessage"/>
/// alongside the content stream. Lets us hand a single <see cref="System.IO.Stream"/>
/// to <see cref="Modules.Stream"/> without leaking the response.
/// </summary>
internal sealed class HttpOwningStream : System.IO.Stream
{
    private readonly HttpResponseMessage _resp;
    private readonly System.IO.Stream _inner;

    public HttpOwningStream(HttpResponseMessage resp, System.IO.Stream inner)
    {
        _resp = resp;
        _inner = inner;
    }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => _inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => _inner.Length;
    public override long Position
    {
        get => _inner.Position;
        set => _inner.Position = value;
    }
    public override void Flush() => _inner.Flush();
    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
        => _inner.ReadAsync(buffer, offset, count, ct);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        => _inner.ReadAsync(buffer, ct);
    public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try { _inner.Dispose(); } catch { }
            try { _resp.Dispose(); } catch { }
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Native fetch implementation. Installs <c>globalThis.fetch</c> as a single
/// callable function (no JS glue) that returns a <c>Promise&lt;FetchResponse&gt;</c>.
/// </summary>
internal static class FetchModule
{
    public static void Install(QuickJSRuntime engine, FetchModuleOptions? options = null)
    {
        options ??= new FetchModuleOptions();
        var capturedOptions = options;

        engine.SetGlobalRawFunction("fetch", (ctx, thisVal, argc, argv) =>
        {
            // Parse arguments synchronously on the JS thread.
            string? url = argc > 0 ? engine.GetString(JSInteropRuntime.ArgAt(argv, 0)) : null;
            if (string.IsNullOrEmpty(url))
                return JSPromiseBridge.RejectedPromise(engine, "url required");

            string method = "GET";
            byte[]? body = null;
            int timeoutMs = capturedOptions.DefaultTimeoutMs;
            string? proxyUrl = capturedOptions.ProxyUrl;
            bool ignoreSsl = capturedOptions.IgnoreSslErrors;
            Dictionary<string, string>? headers = null;

            if (argc > 1)
            {
                var opts = JSInteropRuntime.ArgAt(argv, 1);
                if (opts.IsObject)
                {
                    var mVal = QuickJSNative.QJS_GetPropertyStr(ctx, opts, "method");
                    if (mVal.IsString) method = engine.GetString(mVal) ?? "GET";
                    QuickJSNative.QJS_FreeValue(ctx, mVal);

                    var bVal = QuickJSNative.QJS_GetPropertyStr(ctx, opts, "body");
                    if (bVal.IsString) body = engine.GetStringBytesUTF8(bVal);
                    else if (engine.GetByteArray(bVal) is byte[] bArr) body = bArr;
                    else if (!bVal.IsNullOrUndefined)
                    {
                        var bJson = QuickJSNative.QJS_JSONStringify(ctx, bVal);
                        if (bJson.IsString) body = engine.GetStringBytesUTF8(bJson);
                        QuickJSNative.QJS_FreeValue(ctx, bJson);
                    }
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

                    var hVal = QuickJSNative.QJS_GetPropertyStr(ctx, opts, "headers");
                    if (hVal.IsObject)
                    {
                        // Use JSON.stringify + parse to enumerate keys without
                        // a native key enumeration helper.
                        var hJson = QuickJSNative.QJS_JSONStringify(ctx, hVal);
                        if (hJson.IsString)
                        {
                            var json = engine.GetString(hJson);
                            if (!string.IsNullOrEmpty(json))
                            {
                                try
                                {
                                    headers = System.Text.Json.JsonSerializer
                                        .Deserialize<Dictionary<string, string>>(json);
                                }
                                catch { /* ignore */ }
                            }
                        }
                        QuickJSNative.QJS_FreeValue(ctx, hJson);
                    }
                    QuickJSNative.QJS_FreeValue(ctx, hVal);
                }
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                return JSPromiseBridge.RejectedPromise(engine,
                    "Invalid URL: only http and https schemes are allowed");
            }

            var capturedUrl = url!;
            var capturedMethod = method;
            var capturedBody = body;
            var capturedHeaders = headers;
            var capturedTimeoutMs = timeoutMs;
            var capturedProxy = proxyUrl;
            var capturedIgnoreSsl = ignoreSsl;

            var task = Task.Run(() =>
            {
                var client = GetClient(capturedProxy, capturedIgnoreSsl, capturedTimeoutMs);
                return DoFetch(uri, capturedMethod, capturedBody, capturedHeaders, client, capturedUrl);
            });

            return JSPromiseBridge.FromTask(engine, task, (rt, resp) => default);
        }, argCount: 2);
    }

    private static readonly Lock _clientPoolLock = new();
    private static readonly ConcurrentDictionary<string, HttpClient> _clientPool = [];

    private static HttpClient GetClient(string? proxy, bool ignoreSslError, int timeoutMs)
    {
        string key = $"{proxy ?? "direct"}|{ignoreSslError}|{timeoutMs}";
        if (_clientPool.TryGetValue(key, out var existing)) return existing;
        lock (_clientPoolLock)
        {
            if (_clientPool.TryGetValue(key, out existing)) return existing;
            var handler = new HttpClientHandler();
            if (!string.IsNullOrEmpty(proxy))
            {
                handler.Proxy = new WebProxy(proxy);
                handler.UseProxy = true;
            }
            else
            {
                handler.UseProxy = false;
            }
            if (ignoreSslError)
                handler.ServerCertificateCustomValidationCallback = (m, c, ch, e) => true;
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };
            client.DefaultRequestHeaders.UserAgent.TryParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) " +
                "Chrome/115.0.0.0 Safari/537.36 QJS-f/1.0");
            _clientPool[key] = client;
            return client;
        }
    }

    private static FetchResponse DoFetch(Uri uri, string method, byte[]? body,
        Dictionary<string, string>? headers, HttpClient client, string originalUrl)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), uri);
        if (headers != null)
        {
            foreach (var kvp in headers)
            {
                if (!request.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value))
                {
#if DEBUG
                    Debug.WriteLine($"WRN! Cannot add request header `{kvp.Key}: {kvp.Value}`");
#endif
                }
            }
        }
        if (body != null)
        {
            request.Content = new ByteArrayContent(body);
            if (headers != null)
                foreach (var kvp in headers)
                    request.Content.Headers.TryAddWithoutValidation(kvp.Key, kvp.Value);
        }
        var response = client.SendAsync(request).GetAwaiter().GetResult();
        return new FetchResponse(response, originalUrl);
    }
}
