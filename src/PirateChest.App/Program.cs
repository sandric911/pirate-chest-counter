using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using PirateChest.Core.Counting;

namespace PirateChest.App;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int pid);

    [STAThread]
    private static int Main(string[] args)
    {
        var a = args.Select(x => x.ToLowerInvariant()).ToArray();

        if (a.Contains("--doctor") || a.Contains("--install") || a.Contains("--uninstall") || a.Contains("--version"))
        {
            SetupConsole();
            if (a.Contains("--version"))
            {
                Console.WriteLine(AppInfo.VersionText);
                return 0;
            }
            if (a.Contains("--doctor"))
            {
                return Doctor.Run();
            }
            if (a.Contains("--install"))
            {
                return Installer.Install();
            }
            return Installer.Uninstall(purge: a.Contains("--purge"));
        }

        if (a.Length >= 2 && a[0] == "--apply-update")
        {
            return Updater.ApplyUpdateHelper(a[1]);
        }

        // Double-click on a downloaded copy (not the installed one): install it first.
        if (a.Length == 0 && !AppPaths.RunningInstalled && Environment.GetEnvironmentVariable("PCC_NO_AUTOINSTALL") != "1")
        {
            ApplicationConfiguration.Initialize();
            return Installer.InstallWithDialog();
        }

        return RunApp(background: a.Contains("--background"), updated: a.Contains("--updated"));
    }

    /// <summary>WinExe has no console: write to the caller's pipe if redirected, else attach to its console.</summary>
    private static void SetupConsole()
    {
        if (!Console.IsOutputRedirected)
        {
            AttachConsole(-1);
        }
        try
        {
            Console.OutputEncoding = new UTF8Encoding(false);
        }
        catch
        {
            // no console attached
        }
    }

    private static int RunApp(bool background, bool updated)
    {
        using var mutex = SingleInstance.TryAcquire();
        if (mutex == null)
        {
            // Already running: just show the statistics.
            var url = SingleInstance.RunningUrl();
            if (!background && url != null)
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            return 0;
        }

        Log.Info($"start {AppInfo.VersionText} from {AppPaths.CurrentExe} (background={background}, updated={updated})");
        Updater.CleanupLeftovers();
        ApplicationConfiguration.Initialize();

        var db = new Database(AppPaths.DbPath);
        var atlas = db.LoadAtlas();
        SeedAtlas(atlas);
        var services = new AppServices
        {
            Db = db,
            Atlas = atlas,
            Processor = new ReplayProcessor(db, atlas),
            Fortnite = new FortniteMonitor(db),
            Updater = new Updater(),
        };

        var web = new WebServer();
        try
        {
            web.StartAsync(services).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Log.Error("web server", ex);
            MessageBox.Show("Не удалось запустить локальный веб-интерфейс: " + ex.Message, AppInfo.DisplayName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        SingleInstance.Publish(web.Port);
        services.Processor.Start();

        using var exitEvent = SingleInstance.CreateExitEvent();
        exitEvent.Reset();
        var openBrowser = !background && Environment.GetEnvironmentVariable("PCC_NO_BROWSER") != "1";
        using var tray = new TrayApp(services, web.Url, openBrowser, exitEvent);
        Application.Run(tray);

        Log.Info("exit");
        services.Updater.Dispose();
        services.Fortnite.Dispose();
        services.Processor.Dispose();
        web.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        try
        {
            File.Delete(AppPaths.InstanceFile);
        }
        catch
        {
            // ignore
        }
        return 0;
    }

    /// <summary>Starts with chest locations learned from the author's replays (map coordinates only).</summary>
    private static void SeedAtlas(ChestAtlas atlas)
    {
        if (atlas.Entries.Count > 0)
        {
            return;
        }
        using var s = typeof(Program).Assembly.GetManifestResourceStream("atlas.json");
        if (s == null)
        {
            return;
        }
        try
        {
            var seed = JsonSerializer.Deserialize<Dictionary<string, double[]>>(s);
            foreach (var (k, v) in seed ?? new())
            {
                if (v.Length >= 4)
                {
                    atlas.Set(k, v[0], v[1], v[2], (int)v[3]);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("seed atlas", ex);
        }
    }
}
