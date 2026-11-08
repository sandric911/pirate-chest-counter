using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Win32;

namespace PirateChest.App;

/// <summary>--install / --uninstall. Per-user only: %LOCALAPPDATA%\Programs + HKCU, never admin rights.</summary>
public static class Installer
{
    private const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\" + AppInfo.Name;

    private static string ShortcutPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Programs), AppInfo.DisplayName + ".lnk");

    public static int Install() => Install(Console.Out);

    /// <summary>Double-click on the downloaded exe: install with a message box instead of console output.</summary>
    public static int InstallWithDialog()
    {
        var w = new StringWriter();
        var code = Install(w);
        MessageBox.Show(code == 0
                ? "Счётчик сундуков установлен и запущен.\n\nЗначок — в трее рядом с часами, статистика открылась в браузере. Программа будет запускаться вместе с Windows.\n\nСчитаются только завершённые матчи: включите в Fortnite запись повторов (Настройки → Игра → «Повторы»)."
                : "Установка не удалась:\n\n" + w,
            AppInfo.DisplayName, MessageBoxButtons.OK, code == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        return code;
    }

    public static int Install(TextWriter w)
    {
        w.WriteLine($"Установка {AppInfo.DisplayName} {AppInfo.VersionText}");
        try
        {
            Directory.CreateDirectory(AppPaths.InstallDir);
            Directory.CreateDirectory(AppPaths.DataDir);

            if (!AppPaths.RunningInstalled)
            {
                if (SingleInstance.StopRunning(TimeSpan.FromSeconds(20)))
                {
                    w.WriteLine("  остановлена работающая копия программы");
                }
                CopyWithRetry(AppPaths.CurrentExe, AppPaths.InstalledExe);
                w.WriteLine($"  программа: {AppPaths.InstalledExe}");
            }
            else
            {
                w.WriteLine($"  программа уже в {AppPaths.InstallDir}");
            }

            Autostart.Enable(AppPaths.InstalledExe);
            w.WriteLine("  автозапуск: включён (HKCU\\...\\Run)");

            RegisterUninstall();
            w.WriteLine("  добавлена в «Установленные приложения» Windows");

            if (TryCreateShortcut())
            {
                w.WriteLine("  ярлык в меню «Пуск»: " + AppInfo.DisplayName);
            }

            if (!SingleInstance.IsRunning())
            {
                Detached.Start(AppPaths.InstalledExe);
            }
            var url = WaitForInstance(TimeSpan.FromSeconds(30));
            if (url != null)
            {
                w.WriteLine($"  запущена, статистика: {url}");
                w.WriteLine("Готово.");
                return 0;
            }
            w.WriteLine("  программа установлена, но не ответила за 30 секунд — запустите --doctor");
            return 1;
        }
        catch (Exception ex)
        {
            w.WriteLine("ОШИБКА установки: " + ex.Message);
            Log.Error("install", ex);
            return 1;
        }
    }

    public static int Uninstall(bool purge)
    {
        var w = Console.Out;
        w.WriteLine($"Удаление {AppInfo.DisplayName}");
        try
        {
            if (SingleInstance.StopRunning(TimeSpan.FromSeconds(20)))
            {
                w.WriteLine("  программа остановлена");
            }
            Autostart.Disable();
            w.WriteLine("  автозапуск выключен");
            Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, throwOnMissingSubKey: false);
            if (File.Exists(ShortcutPath))
            {
                File.Delete(ShortcutPath);
                w.WriteLine("  ярлык удалён");
            }

            var dirs = new List<string> { AppPaths.InstallDir };
            if (purge)
            {
                dirs.Add(AppPaths.DataDir);
            }

            if (AppPaths.RunningInstalled || purge)
            {
                // We may be running from the folder being removed: delete after we exit.
                var cmd = "/c ping 127.0.0.1 -n 3 > nul & " + string.Join(" & ", dirs.Select(d => $"rmdir /s /q \"{d}\""));
                Detached.Start(Path.Combine(Environment.SystemDirectory, "cmd.exe"), cmd, hidden: true);
                w.WriteLine($"  папка программы будет удалена через пару секунд: {AppPaths.InstallDir}");
            }
            else if (Directory.Exists(AppPaths.InstallDir))
            {
                Directory.Delete(AppPaths.InstallDir, true);
                w.WriteLine($"  удалена папка {AppPaths.InstallDir}");
            }

            w.WriteLine(purge
                ? $"  данные (база, настройки) удаляются: {AppPaths.DataDir}"
                : $"  данные оставлены: {AppPaths.DataDir} (удалить вместе с ними: --uninstall --purge)");
            w.WriteLine("  реплеи Fortnite не тронуты.");
            w.WriteLine("Готово.");
            return 0;
        }
        catch (Exception ex)
        {
            w.WriteLine("ОШИБКА удаления: " + ex.Message);
            Log.Error("uninstall", ex);
            return 1;
        }
    }

    private static void CopyWithRetry(string from, string to)
    {
        for (var i = 0; ; i++)
        {
            try
            {
                File.Copy(from, to, true);
                return;
            }
            catch (IOException) when (i < 40)
            {
                Thread.Sleep(500);
            }
            catch (UnauthorizedAccessException) when (i < 40)
            {
                Thread.Sleep(500);
            }
        }
    }

    private static void RegisterUninstall()
    {
        using var k = Registry.CurrentUser.CreateSubKey(UninstallKey);
        k.SetValue("DisplayName", AppInfo.DisplayName);
        k.SetValue("DisplayVersion", AppInfo.VersionText);
        k.SetValue("Publisher", "pirate-chest-counter (неофициальный фанатский инструмент)");
        k.SetValue("DisplayIcon", AppPaths.InstalledExe);
        k.SetValue("InstallLocation", AppPaths.InstallDir);
        k.SetValue("UninstallString", $"\"{AppPaths.InstalledExe}\" --uninstall");
        k.SetValue("URLInfoAbout", $"https://github.com/{AppInfo.UpdateRepo}");
        k.SetValue("NoModify", 1, RegistryValueKind.DWord);
        k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        try
        {
            k.SetValue("EstimatedSize", (int)(new FileInfo(AppPaths.InstalledExe).Length / 1024), RegistryValueKind.DWord);
        }
        catch
        {
            // optional
        }
    }

    private static bool TryCreateShortcut()
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
            {
                return false;
            }
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic link = shell.CreateShortcut(ShortcutPath);
            link.TargetPath = AppPaths.InstalledExe;
            link.WorkingDirectory = AppPaths.InstallDir;
            link.Description = "Счётчик сундуков Fortnite";
            link.IconLocation = AppPaths.InstalledExe + ",0";
            link.Save();
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("shortcut", ex);
            return false;
        }
    }

    /// <summary>Waits until the running instance answers /api/ping; returns its URL.</summary>
    public static string? WaitForInstance(TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        while (sw.Elapsed < timeout)
        {
            var url = SingleInstance.RunningUrl();
            if (url != null)
            {
                try
                {
                    var r = http.GetStringAsync(url + "api/ping").GetAwaiter().GetResult();
                    if (r.Contains(AppInfo.Name, StringComparison.Ordinal))
                    {
                        return url;
                    }
                }
                catch
                {
                    // not up yet
                }
            }
            Thread.Sleep(500);
        }
        return null;
    }
}

/// <summary>One instance per user. The running instance publishes its port in instance.json.</summary>
public static class SingleInstance
{
    private const string MutexName = @"Local\PirateChestCounter.Running";
    private const string ExitEventName = @"Local\PirateChestCounter.Exit";

    public static Mutex? TryAcquire()
    {
        var m = new Mutex(true, MutexName, out var created);
        if (created)
        {
            return m;
        }
        m.Dispose();
        return null;
    }

    public static bool IsRunning()
    {
        if (Mutex.TryOpenExisting(MutexName, out var m))
        {
            m.Dispose();
            return true;
        }
        return false;
    }

    public static EventWaitHandle CreateExitEvent() => new(false, EventResetMode.ManualReset, ExitEventName);

    /// <summary>Asks a running instance to exit and waits for it. Returns true if one was running.</summary>
    public static bool StopRunning(TimeSpan timeout)
    {
        if (!IsRunning())
        {
            return false;
        }
        if (EventWaitHandle.TryOpenExisting(ExitEventName, out var ev))
        {
            using (ev)
            {
                ev.Set();
            }
        }
        var sw = Stopwatch.StartNew();
        while (IsRunning() && sw.Elapsed < timeout)
        {
            Thread.Sleep(250);
        }
        Thread.Sleep(500); // let the process release the exe file
        return true;
    }

    public static void Publish(int port)
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        File.WriteAllText(AppPaths.InstanceFile, JsonSerializer.Serialize(new { port, pid = Environment.ProcessId, version = AppInfo.VersionText }));
    }

    public static string? RunningUrl()
    {
        try
        {
            if (!File.Exists(AppPaths.InstanceFile))
            {
                return null;
            }
            using var doc = JsonDocument.Parse(File.ReadAllText(AppPaths.InstanceFile));
            return $"http://127.0.0.1:{doc.RootElement.GetProperty("port").GetInt32()}/";
        }
        catch
        {
            return null;
        }
    }
}
