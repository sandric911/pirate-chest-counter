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
            if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method) && ctx.Request.Headers["X-PCC"] != "1")
            {
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
        app.MapGet("/api/summary", () => Results.Json(stats.Summary(s.Processor.Status)));
        app.MapGet("/api/matches", () => Results.Json(stats.MatchList()));
        app.MapGet("/api/daily", () => Results.Json(stats.Daily()));
        app.MapGet("/api/efficiency", () => Results.Json(stats.Efficiency()));
        app.MapGet("/api/map/points", (string? match) => Results.Json(stats.MapPoints(string.IsNullOrEmpty(match) ? null : match)));
        app.MapGet("/api/map/calibration", () => Results.Text(s.Db.Get("map_calibration") ?? DefaultCalibration(), "application/json"));
        app.MapGet("/api/status", () => Results.Json(StatusInfo(s, stats)));
        app.MapPost("/api/recount", () =>
        {
            s.Processor.RecountAll();
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
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".json" => "application/json",
            ".woff2" => "font/woff2",
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
