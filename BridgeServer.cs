using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Skuload.PrintBridge;

/// <summary>
/// The localhost HTTP surface the web app talks to. Binds to 127.0.0.1 only
/// (never a routable interface), so it isn't reachable from the network. CORS
/// is open — any origin is reflected — and it answers the Chrome Private
/// Network Access preflight so an HTTPS page can reach it. Routes:
/// <list type="bullet">
///   <item><c>GET  /ping</c> — liveness + capabilities (printers, version).</item>
///   <item><c>GET  /printers</c> — installed printer names.</item>
///   <item><c>POST /print</c> — print a base64 label.</item>
/// </list>
/// </summary>
internal sealed class BridgeServer : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Settings _settings;
    private readonly CancellationTokenSource _cts = new();
    private HttpListener? _listener;

    /// <summary>The port we actually bound to (may differ from the request if it was busy).</summary>
    public int BoundPort { get; private set; }

    public BridgeServer(Settings settings) => _settings = settings;

    /// <summary>
    /// Bind and start serving. Tries the configured port and a small range above
    /// it if that one is taken. Returns the port we landed on.
    /// </summary>
    public int Start()
    {
        Exception? last = null;
        for (var port = _settings.Port; port <= _settings.Port + 10 && port <= 65535; port++)
        {
            try
            {
                var listener = new HttpListener();
                listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                listener.Start();
                _listener = listener;
                BoundPort = port;
                if (port != _settings.Port)
                {
                    Logger.Warn($"Port {_settings.Port} busy; bound to {port} instead.");
                    _settings.Port = port;
                    _settings.Save();
                }
                _ = Task.Run(AcceptLoopAsync);
                Logger.Info($"Listening on http://127.0.0.1:{port}/");
                return port;
            }
            catch (Exception ex)
            {
                last = ex;
            }
        }

        throw new InvalidOperationException(
            $"Could not bind any port in {_settings.Port}..{_settings.Port + 10}.", last);
    }

    private async Task AcceptLoopAsync()
    {
        var listener = _listener!;
        while (!_cts.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (_cts.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Error("Accept failed", ex);
                continue;
            }

            // Handle off-thread so a slow print never blocks the next request.
            _ = Task.Run(() => HandleSafely(ctx));
        }
    }

    private void HandleSafely(HttpListenerContext ctx)
    {
        try
        {
            Handle(ctx);
        }
        catch (Exception ex)
        {
            Logger.Error("Request handling failed", ex);
            TryWriteError(ctx, 500, ex.Message);
        }
        finally
        {
            try { ctx.Response.Close(); } catch { /* client gone */ }
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var origin = req.Headers["Origin"];
        ApplyCors(ctx, origin);

        if (string.Equals(req.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            ApplyPreflight(ctx, req);
            ctx.Response.StatusCode = 204;
            return;
        }

        var path = req.Url?.AbsolutePath.TrimEnd('/').ToLowerInvariant();
        switch (req.HttpMethod.ToUpperInvariant(), path)
        {
            case ("GET", "" or "/ping"):
                WriteJson(ctx, 200, Info());
                break;

            case ("GET", "/printers"):
                WriteJson(ctx, 200, new { ok = true, printers = PrintService.InstalledPrinters() });
                break;

            case ("POST", "/print"):
                HandlePrint(ctx);
                break;

            default:
                WriteJson(ctx, 404, new { ok = false, error = "Not found." });
                break;
        }
    }

    private void HandlePrint(HttpListenerContext ctx)
    {
        PrintRequest? body;
        using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8))
        {
            var raw = reader.ReadToEnd();
            body = JsonSerializer.Deserialize<PrintRequest>(raw, JsonOpts);
        }

        if (body is null || string.IsNullOrWhiteSpace(body.LabelBase64))
        {
            WriteJson(ctx, 400, new { ok = false, error = "labelBase64 is required." });
            return;
        }

        if (!TryDecodeBase64(body.LabelBase64, out var bytes))
        {
            WriteJson(ctx, 400, new { ok = false, error = "labelBase64 is not valid base64." });
            return;
        }

        var opts = PrintOptions.FromSettings(_settings, body.Printer, body.Copies);
        var (used, pages) = PrintService.Print(bytes, body.Format, opts);
        Logger.Info($"Printed {pages} page(s) to '{used}' ({bytes.Length} bytes).");
        WriteJson(ctx, 200, new
        {
            ok = true,
            jobId = Guid.NewGuid().ToString("N"),
            printer = used,
            pages,
        });
    }

    private object Info() => new
    {
        ok = true,
        name = "skuload-print-bridge",
        version = typeof(BridgeServer).Assembly.GetName().Version?.ToString() ?? "1.0.0",
        status = "ready",
        port = BoundPort,
        printers = PrintService.InstalledPrinters(),
        defaultPrinter = PrintService.DefaultPrinter(),
        selectedPrinter = string.IsNullOrWhiteSpace(_settings.PrinterName)
            ? PrintService.DefaultPrinter()
            : _settings.PrinterName,
    };

    // ---- CORS / Private Network Access ------------------------------------

    private void ApplyCors(HttpListenerContext ctx, string? origin)
    {
        var res = ctx.Response;
        // No origin allow-list: reflect whatever origin asked (or "*" for
        // non-browser callers). The server only listens on loopback anyway.
        if (!string.IsNullOrEmpty(origin))
        {
            res.Headers["Access-Control-Allow-Origin"] = origin;
            res.Headers["Vary"] = "Origin";
        }
        else
        {
            res.Headers["Access-Control-Allow-Origin"] = "*";
        }
        res.Headers["Cache-Control"] = "no-store";
    }

    private void ApplyPreflight(HttpListenerContext ctx, HttpListenerRequest req)
    {
        var res = ctx.Response;
        res.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        var requested = req.Headers["Access-Control-Request-Headers"];
        res.Headers["Access-Control-Allow-Headers"] =
            string.IsNullOrEmpty(requested) ? "Content-Type" : requested;
        res.Headers["Access-Control-Max-Age"] = "86400";

        // Chrome Private Network Access: a public/secure page reaching localhost
        // must get explicit opt-in or the request is blocked.
        if (string.Equals(req.Headers["Access-Control-Request-Private-Network"], "true",
                StringComparison.OrdinalIgnoreCase))
        {
            res.Headers["Access-Control-Allow-Private-Network"] = "true";
        }
    }

    // ---- helpers ----------------------------------------------------------

    private static bool TryDecodeBase64(string input, out byte[] bytes)
    {
        var s = input.Trim();
        // Strip a data: URL prefix if present.
        var comma = s.IndexOf(',');
        if (s.StartsWith("data:", StringComparison.OrdinalIgnoreCase) && comma >= 0)
        {
            s = s[(comma + 1)..];
        }
        s = s.Replace("\r", string.Empty).Replace("\n", string.Empty).Replace(" ", string.Empty);
        // url-safe -> standard
        s = s.Replace('-', '+').Replace('_', '/');
        if (s.Length % 4 != 0)
        {
            s = s.PadRight(s.Length + (4 - s.Length % 4), '=');
        }

        try
        {
            bytes = Convert.FromBase64String(s);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }

    private static void WriteJson(HttpListenerContext ctx, int status, object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOpts);
        var buffer = Encoding.UTF8.GetBytes(json);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = buffer.Length;
        ctx.Response.OutputStream.Write(buffer, 0, buffer.Length);
    }

    private static void TryWriteError(HttpListenerContext ctx, int status, string message)
    {
        try
        {
            WriteJson(ctx, status, new { ok = false, error = message });
        }
        catch
        {
            // Response may already be committed; nothing more we can do.
        }
    }

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
            _listener?.Stop();
            _listener?.Close();
        }
        catch
        {
            // best-effort shutdown
        }
        _cts.Dispose();
    }

    private sealed record PrintRequest(string? LabelBase64, string? Format, string? Printer, int? Copies);
}
