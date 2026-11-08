using System.Reflection;

namespace PirateChest.App;

public static class AppInfo
{
    public const string Name = "PirateChestCounter";
    public const string DisplayName = "Pirate Chest Counter";
    public const string ExeName = "PirateChestCounter.exe";

    /// <summary>Bump when counting rules change: matches counted with an older version are recounted.</summary>
    public const int RulesVersion = 1;

    public static Version Version { get; } = typeof(AppInfo).Assembly.GetName().Version ?? new Version(0, 0, 0);
    public static string VersionText => $"{Version.Major}.{Version.Minor}.{Version.Build}";

    /// <summary>owner/repo on GitHub, baked in at build time.</summary>
    public static string UpdateRepo { get; } =
        typeof(AppInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "UpdateRepo")?.Value
        ?? "local/pirate-chest-counter";

    public static string ParserVersion => $"{PirateChest.ReplayReader.ChestReplayReader.ParserVersion}.{RulesVersion}";
}

public static class AppPaths
{
    public static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    // PCC_* environment overrides exist for development/testing only.
    public static string InstallDir => Environment.GetEnvironmentVariable("PCC_INSTALL_DIR") ?? Path.Combine(LocalAppData, "Programs", AppInfo.Name);
    public static string InstalledExe => Path.Combine(InstallDir, AppInfo.ExeName);
    public static string DataDir => Environment.GetEnvironmentVariable("PCC_DATA_DIR") ?? Path.Combine(LocalAppData, AppInfo.Name);
    public static string DbPath => Path.Combine(DataDir, "data.db");
    public static string LogPath => Path.Combine(DataDir, "log.txt");
    public static string InstanceFile => Path.Combine(DataDir, "instance.json");
    public static string DoctorReport => Path.Combine(DataDir, "doctor.txt");
    public static string UpdateDir => Path.Combine(DataDir, "update");
    public static string DemosDir => Environment.GetEnvironmentVariable("PCC_DEMOS_DIR") ?? Path.Combine(LocalAppData, "FortniteGame", "Saved", "Demos");

    public static string CurrentExe => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, AppInfo.ExeName);

    public static bool RunningInstalled =>
        string.Equals(Path.GetFullPath(CurrentExe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);
}

public static class Log
{
    private static readonly object Gate = new();

    public static void Info(string msg) => Write("INFO", msg);
    public static void Error(string msg, Exception? ex = null) => Write("ERROR", ex == null ? msg : $"{msg}: {ex}");

    private static void Write(string level, string msg)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.DataDir);
                var fi = new FileInfo(AppPaths.LogPath);
                if (fi.Exists && fi.Length > 2_000_000)
                {
                    File.Move(AppPaths.LogPath, AppPaths.LogPath + ".old", true);
                }
                File.AppendAllText(AppPaths.LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {msg}{Environment.NewLine}");
            }
        }
        catch
        {
            // logging must never crash the app
        }
    }
}

public static class Detached
{
    /// <summary>
    /// Starts a process through ShellExecute so it does NOT inherit our handles. Otherwise a caller that
    /// pipes our output (e.g. `--install | Out-Host`) would wait until the started app exits.
    /// </summary>
    public static void Start(string exe, string arguments = "", bool hidden = false)
    {
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, arguments)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
            WindowStyle = hidden ? System.Diagnostics.ProcessWindowStyle.Hidden : System.Diagnostics.ProcessWindowStyle.Normal,
        });
    }
}
