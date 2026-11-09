using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PirateChest.Core.Counting;

namespace PirateChest.App;

/// <summary>Local web UI + JSON API. Listens on 127.0.0.1 only; static files are embedded in the exe.</summary>
public sealed class WebServer : IAsyncDisposable
{
    public const int PreferredPort = 47123;
    private WebApplication? _app;

    public int Port { get; private set; }
    public string Url => $"http://127.0.0.1:{Port}/";

    /// <summary>Per-run secret: only the app window (which is given it) can open the UI, not a browser or another program.</summary>
    public string Token { get; } = Environment.GetEnvironmentVariable("PCC_DEV_TOKEN") is { Length: >= 16 } t
        ? t // development only: lets a test script open the UI the way the window does
        : Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));

    /// <summary>The address the app window opens first: it trades the token for a cookie and lands on the UI.</summary>
    public string WindowUrl => $"{Url}__auth?t={Token}";

    public static int PickPort()
    {
        for (var p = PreferredPort; p < PreferredPort + 10; p++)
        {
            if (IsFree(p))
            {
                return p;
            }
        }
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public static bool IsFree(int port)
    {
        try
        {
            var l = new TcpListener(IPAddress.Loopback, port);
            l.Start();
            l.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public async Task StartAsync(AppServices s)
    {
        Port = PickPort();
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(IPAddress.Loopback, Port));
        builder.Services.ConfigureHttpJsonOptions(o =>
        {
            o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            o.SerializerOptions.NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals;
        });
        var app = builder.Build();
        var port = Port;
        var token = Token;
        var open = Environment.GetEnvironmentVariable("PCC_DEV_OPEN") == "1"; // development: screenshots from a browser

        // Only answer requests addressed to this machine (blocks DNS rebinding), and require a custom
        // header on state-changing requests (a foreign web page cannot send it without CORS, which we never enable).
        app.Use(async (ctx, next) =>
        {
            var host = ctx.Request.Host.Host;
            if (host is not ("127.0.0.1" or "localhost"))
            {
                ctx.Response.StatusCode = 403;
                return;
            }
            if (ctx.Request.Path == "/__auth")
            {
                if (ctx.Request.Query["t"] == token)
                {
                    ctx.Response.Cookies.Append("pcc", token, new CookieOptions { HttpOnly = true, SameSite = SameSiteMode.Strict, Path = "/" });
                    ctx.Response.Redirect("/");
                }
                else
                {
                    ctx.Response.StatusCode = 403;
                }
                return;
            }
            // /api/ping (name, version, pid) stays open: the installer and --doctor use it to see that the app is up.
            if (!open && ctx.Request.Path != "/api/ping" && ctx.Request.Cookies["pcc"] != token)
            {
                Log.Info($"refused {ctx.Request.Method} {ctx.Request.Path}: {(ctx.Request.Cookies.ContainsKey("pcc") ? "stale" : "no")} window key");
                ctx.Response.StatusCode = 403;
                return;
            }
            if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && ctx.Request.Headers["X-PCC"] != "1")
            {
                Log.Info($"refused {ctx.Request.Method} {ctx.Request.Path}: no X-PCC header");
                ctx.Response.StatusCode = 403;
                return;
            }
            ctx.Response.Headers["Cache-Control"] = "no-store";
            ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
            ctx.Response.Headers["Content-Security-Policy"] = "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; connect-src 'self'";
            await next();
        });

        var stats = new Stats(s.Db);

        app.MapGet("/api/ping", () => Results.Json(new { app = AppInfo.Name, version = AppInfo.VersionText, pid = Environment.ProcessId }));
        app.MapGet("/api/summary", () => Results.Json(stats.Summary(s.Processor.Status, s.Fortnite.Running)));
        app.MapGet("/api/matches", () => Results.Json(stats.MatchList()));
        app.MapGet("/api/daily", () => Results.Json(stats.Daily()));
        app.MapGet("/api/efficiency", () => Results.Json(stats.Efficiency()));
        app.MapGet("/api/map/chests", (string? match) => Results.Json(stats.MapChests(string.IsNullOrEmpty(match) ? null : match, s.Atlas)));
        app.MapGet("/api/map/calibration", () => Results.Text(s.Db.Get("map_calibration") ?? DefaultCalibration(), "application/json"));
        app.MapGet("/api/status", () => Results.Json(StatusInfo(s, stats)));
        app.MapPost("/api/recount", () =>
        {
            s.Processor.RecountAll();
            return Results.Ok();
        });
        app.MapGet("/api/overlay", () =>
        {
            var o = OverlaySettings.Load(s.Db);
            var (value, delta) = stats.OverlayNumbers(o.Show, s.Processor.Status, s.Fortnite.Running);
            return Results.Json(new { settings = o, value, delta, moving = s.Ui.OverlayMoving, fortniteRunning = s.Fortnite.Running });
        });
        app.MapPost("/api/overlay", (OverlaySettings body) =>
        {
            var old = OverlaySettings.Load(s.Db);
            body.X ??= old.X; // the position is changed by dragging the overlay, not by the form
            body.Y ??= old.Y;
            body.Normalized().Save(s.Db);
            s.Ui.ChangeOverlay(body.Clone());
            return Results.Json(body);
        });
        app.MapPost("/api/overlay/move", (MoveBody body) =>
        {
            s.Ui.SetOverlayMove(body.On);
            return Results.Ok();
        });
        app.MapPost("/api/overlay/reset-position", () =>
        {
            var o = OverlaySettings.Load(s.Db);
            o.X = null;
            o.Y = null;
            o.Save(s.Db);
            s.Ui.ChangeOverlay(o.Clone());
            return Results.Ok();
        });
        app.MapPost("/api/update/check", async () =>
        {
            await s.Updater.CheckAsync(manual: true);
            return Results.Json(s.Updater.State);
        });

        app.MapGet("/{**path}", (string? path) => ServeEmbedded(path));

        await app.StartAsync();
        _app = app;
        Log.Info($"web ui at {Url}");
    }

    private static string DefaultCalibration()
    {
        using var st = typeof(WebServer).Assembly.GetManifestResourceStream("wwwroot/calibration.json");
        return st == null ? "null" : new StreamReader(st).ReadToEnd();
    }

    private static object StatusInfo(AppServices s, Stats stats)
    {
        var ps = s.Processor.Status;
        return new
        {
            version = AppInfo.VersionText,
            repo = AppInfo.UpdateRepo,
            update = s.Updater.State,
            demosDir = ps.DemosDir,
            demosFound = ps.DemosFound,
            replayFiles = ps.ReplayFiles,
            newestReplayUtc = ps.NewestReplayUtc,
            lastScanUtc = ps.LastScanUtc,
            progress = new { busy = ps.Current != null || ps.Pending > 0, current = ps.Current, done = ps.BatchDone, total = ps.BatchTotal, pending = ps.Pending, live = ps.Live },
            errors = ps.Errors,
            backups = ps.Backups,
            backupBytes = ps.BackupBytes,
            recordingWarning = s.Fortnite.RecordingWarning(ps),
            fortniteRunning = s.Fortnite.Running,
            autostart = Autostart.IsEnabled(),
            installed = AppPaths.RunningInstalled,
            dataDir = AppPaths.DataDir,
            hasMap = typeof(WebServer).Assembly.GetManifestResourceInfo("wwwroot/map.png") != null || File.Exists(Path.Combine(AppPaths.DataDir, "map.png")),
        };
    }

    public sealed record MoveBody(bool On);

    /// <summary>Embedded file names, normalised to '/' (MSBuild keeps a backslash for sub-folders such as fonts).</summary>
    private static readonly Dictionary<string, string> Resources = typeof(WebServer).Assembly.GetManifestResourceNames()
        .GroupBy(n => n.Replace('\\', '/'), StringComparer.OrdinalIgnoreCase)
        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    private static IResult ServeEmbedded(string? path)
    {
        path = string.IsNullOrEmpty(path) ? "index.html" : path.Replace('\\', '/');
        if (path.Contains("..", StringComparison.Ordinal))
        {
            return Results.NotFound();
        }
        if (path == "map.png" && File.Exists(Path.Combine(AppPaths.DataDir, "map.png")))
        {
            return Results.File(Path.Combine(AppPaths.DataDir, "map.png"), "image/png");
        }
        var asm = typeof(WebServer).Assembly;
        var stream = Resources.TryGetValue("wwwroot/" + path, out var resName) ? asm.GetManifestResourceStream(resName) : null;
        if (stream == null)
        {
            if (!path.Contains('.'))
            {
                stream = asm.GetManifestResourceStream("wwwroot/index.html"); // client-side routes
                path = "index.html";
            }
            if (stream == null)
            {
                return Results.NotFound();
            }
        }
        var type = Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".json" => "application/json",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            _ => "application/octet-stream",
        };
        return Results.Stream(stream, type);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync(TimeSpan.FromSeconds(3));
            await _app.DisposeAsync();
        }
    }

}
