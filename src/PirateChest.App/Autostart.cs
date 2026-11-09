using Microsoft.Win32;

namespace PirateChest.App;

/// <summary>
/// Start with Windows, without admin rights. Three per-user pieces, because on the author's PC Windows skipped the
/// HKCU Run value alone at sign-in (Shell-Core log: the Run key was enumerated, our command never executed):
/// the HKCU Run value, its "enabled" mark in StartupApproved (what Task Manager writes) and a shortcut in the
/// user's Startup folder. Two of them may both start the program; the single-instance mutex keeps only one.
/// </summary>
public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>StartupApproved value for "enabled": 02 followed by zeros (03 = disabled by the user).</summary>
    private static readonly byte[] Approved = { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    public static string Command(string exe) => $"\"{exe}\" --background";

    private static string ShortcutPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), AppInfo.DisplayName + ".lnk");

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
        using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
        {
            k.SetValue(AppInfo.Name, Command(exe));
        }
        Repair(exe, force: true); // the user (or the installer) asked for it: mark it enabled even if it was switched off
    }

    /// <summary>Makes sure the approval mark and the Startup-folder shortcut exist next to the Run value.</summary>
    public static void Repair(string exe, bool force = false)
    {
        try
        {
            using var a = Registry.CurrentUser.CreateSubKey(ApprovedKey);
            // Respect a user who switched it off in Task Manager / Settings (03…): then add nothing.
            if (!force && a.GetValue(AppInfo.Name) is byte[] { Length: > 0 } cur && cur[0] != 2)
            {
                return;
            }
            a.SetValue(AppInfo.Name, Approved, RegistryValueKind.Binary);
        }
        catch (Exception ex)
        {
            Log.Error("autostart approval", ex);
        }
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                return;
            }
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic link = shell.CreateShortcut(ShortcutPath);
            link.TargetPath = exe;
            link.Arguments = "--background";
            link.WorkingDirectory = Path.GetDirectoryName(exe);
            link.Description = "Fortnite chest counter";
            link.IconLocation = exe + ",0";
            link.Save();
        }
        catch (Exception ex)
        {
            Log.Error("autostart shortcut", ex);
        }
    }

    public static void Disable()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
        {
            k?.DeleteValue(AppInfo.Name, throwOnMissingValue: false);
        }
        using (var a = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true))
        {
            a?.DeleteValue(AppInfo.Name, throwOnMissingValue: false);
        }
        try
        {
            if (File.Exists(ShortcutPath))
            {
                File.Delete(ShortcutPath);
            }
        }
        catch (Exception ex)
        {
            Log.Error("autostart shortcut", ex);
        }
    }
}
