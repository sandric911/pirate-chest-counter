using Microsoft.Win32;

namespace PirateChest.App;

/// <summary>HKCU\Software\Microsoft\Windows\CurrentVersion\Run — no admin rights needed.</summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static string Command(string exe) => $"\"{exe}\" --background";

    public static string? Current()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(AppInfo.Name) as string;
    }

    public static bool IsEnabled() => Current() != null;

    /// <summary>True when the Run entry points at the installed exe.</summary>
    public static bool PointsToInstalled() =>
        string.Equals(Current(), Command(AppPaths.InstalledExe), StringComparison.OrdinalIgnoreCase);

    public static void Enable(string exe)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        k.SetValue(AppInfo.Name, Command(exe));
    }

    public static void Disable()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        k?.DeleteValue(AppInfo.Name, throwOnMissingValue: false);
    }
}
