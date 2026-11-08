using System.Diagnostics;
using System.Net.Http;
using System.Text;

namespace PirateChest.App;

/// <summary>--doctor: plain-language self check. Printed to stdout and saved to doctor.txt.</summary>
public static class Doctor
{
    public static int Run()
    {
        var sb = new StringBuilder();
        var problems = new List<string>();
        void Line(string s) => sb.AppendLine(s);
        void Ok(string what, string detail) => Line($"[OK]   {what}: {detail}");
        void Warn(string what, string detail, string? fix = null)
        {
            Line($"[!!]   {what}: {detail}");
            if (fix != null)
            {
                Line($"       Что сделать: {fix}");
            }
            problems.Add(what);
        }

        Line($"{AppInfo.DisplayName} — самопроверка ({DateTime.Now:dd.MM.yyyy HH:mm})");
        Line("");
        Ok("Версия", $"{AppInfo.VersionText}, обновления из github.com/{AppInfo.UpdateRepo}");

        var os = Environment.OSVersion.Version;
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && Environment.Is64BitOperatingSystem)
        {
            Ok("Windows", $"Windows 11 x64 (сборка {os.Build})");
        }
        else
        {
            Warn("Windows", $"сборка {os.Build}, 64-бит: {Environment.Is64BitOperatingSystem}", "программа рассчитана на Windows 11 x64");
        }

        if (File.Exists(AppPaths.InstalledExe))
        {
            Ok("Установка", AppPaths.InstalledExe);
        }
        else
        {
            Warn("Установка", "программа не установлена", "запустите PirateChestCounter.exe --install");
        }

        var autostart = Autostart.Current();
        if (autostart == null)
        {
            Warn("Автозапуск", "выключен", "включите в меню значка в трее или запустите --install");
        }
        else if (!Autostart.PointsToInstalled())
        {
            Warn("Автозапуск", $"указывает на другой файл: {autostart}", "запустите --install");
        }
        else
        {
            Ok("Автозапуск", "включён");
        }

        // Replays
        var demos = AppPaths.DemosDir;
        if (!Directory.Exists(demos))
        {
            Warn("Папка повторов", $"{demos} не найдена",
                "в Fortnite: Настройки → Игра → раздел «Повторы» → включите запись повторов, сыграйте матч");
        }
        else
        {
            var files = new DirectoryInfo(demos).GetFiles("*.replay");
            if (files.Length == 0)
            {
                Warn("Папка повторов", $"{demos} пустая", "включите запись повторов в настройках Fortnite и сыграйте матч");
            }
            else
            {
                var newest = files.Max(f => f.LastWriteTime);
                var age = DateTime.Now - newest;
                Ok("Папка повторов", $"{demos}: {files.Length} реплеев, самый свежий {newest:dd.MM.yyyy HH:mm} ({Ago(age)})");
                if (age > TimeSpan.FromDays(3))
                {
                    Warn("Свежесть реплеев", $"новых реплеев не было {Ago(age)}",
                        "если вы играли за это время — проверьте, что запись повторов включена (Настройки → Игра → «Повторы»)");
                }
            }
        }

        // Database
        try
        {
            if (File.Exists(AppPaths.DbPath))
            {
                var db = new Database(AppPaths.DbPath);
                var matches = db.Matches().Where(m => !Stats.IsHidden(m)).ToList();
                var counted = matches.Where(m => m.Status == "counted").ToList();
                var errors = db.Files("error").Count;
                Ok("База", $"{AppPaths.DbPath}: матчей засчитано {counted.Count}, пропущено {matches.Count - counted.Count}, «Мои» = {counted.Sum(m => m.Mine)}");
                if (errors > 0)
                {
                    Warn("Ошибки разбора", $"{errors} реплеев не разобрались",
                        "они пересчитаются автоматически после обновления программы; список — на вкладке «Состояние»");
                }
            }
            else
            {
                Line("[--]   База: ещё не создана (появится после первого запуска)");
            }
        }
        catch (Exception ex)
        {
            Warn("База", ex.Message);
        }

        // Running instance / port
        var url = SingleInstance.IsRunning() ? SingleInstance.RunningUrl() : null;
        if (url != null)
        {
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
                var ping = http.GetStringAsync(url + "api/ping").GetAwaiter().GetResult();
                Ok("Программа запущена", $"{url} отвечает");
            }
            catch (Exception ex)
            {
                Warn("Программа запущена", $"но {url} не отвечает ({ex.Message})", "перезапустите программу из меню «Пуск»");
            }
        }
        else
        {
            Warn("Программа запущена", "нет", "запустите «Pirate Chest Counter» из меню «Пуск» или PirateChestCounter.exe");
            Line(WebServer.IsFree(WebServer.PreferredPort)
                ? $"[OK]   Порт {WebServer.PreferredPort}: свободен"
                : $"[--]   Порт {WebServer.PreferredPort}: занят другой программой — будет выбран запасной");
        }

        // Updates
        try
        {
            var rel = Updater.LatestAsync().GetAwaiter().GetResult();
            if (rel == null)
            {
                Line("[--]   Обновления: в последнем релизе нет файлов программы");
            }
            else if (rel.Version > Updater.CurrentVersion)
            {
                Line($"[--]   Обновления: доступна {rel.Tag}, установится автоматически (в течение 6 часов или при перезапуске)");
            }
            else
            {
                Ok("Обновления", $"последняя версия на GitHub {rel.Tag} — установлена актуальная");
            }
        }
        catch (Exception ex)
        {
            Line($"[--]   Обновления: не удалось проверить ({ex.Message})");
        }

        var fortnite = Process.GetProcessesByName("FortniteClient-Win64-Shipping");
        Line($"[--]   Fortnite сейчас {(fortnite.Length > 0 ? "запущен" : "не запущен")}");
        foreach (var p in fortnite)
        {
            p.Dispose();
        }

        Line("");
        Line(problems.Count == 0 ? "Итог: всё в порядке." : $"Итог: есть замечания ({string.Join(", ", problems)}).");

        var text = sb.ToString();
        Console.Out.Write(text);
        Console.Out.Flush();
        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(AppPaths.DoctorReport, text, new UTF8Encoding(true));
        }
        catch
        {
            // the console copy is enough
        }
        return problems.Count == 0 ? 0 : 1;
    }

    private static string Ago(TimeSpan t) =>
        t.TotalMinutes < 60 ? $"{(int)t.TotalMinutes} мин назад"
        : t.TotalHours < 48 ? $"{(int)t.TotalHours} ч назад"
        : $"{(int)t.TotalDays} дн назад";
}
