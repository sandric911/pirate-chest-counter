using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace PirateChest.App;

public sealed class UpdateState
{
    public string? Latest { get; set; }
    public DateTime? CheckedUtc { get; set; }
    private (string Ru, string En) _status = ("ещё не проверялось", "not checked yet");

    /// <summary>Status text in the current interface language.</summary>
    public string Status => Lang.T(_status.Ru, _status.En);

    public void SetStatus(string ru, string en) => _status = (ru, en);
    public bool Available { get; set; }
}

/// <summary>
/// Checks the latest GitHub release at start and every 6 hours. Talks only to GitHub
/// (api.github.com and the release download host it redirects to). A new exe is verified
/// against SHA256SUMS.txt from the same release, then swapped in by a helper process.
/// </summary>
public sealed class Updater : IDisposable
{
    public const string AssetName = "PirateChestCounter.exe";
    public const string SumsName = "SHA256SUMS.txt";
    public const string HelperName = "PirateChestCounter.update.exe";
    private static readonly HttpClient Http = CreateClient();
    private readonly System.Threading.Timer _timer;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public UpdateState State { get; } = new();

    /// <summary>Returns false while a replay is being processed.</summary>
    public Func<bool>? CanApplyNow { get; set; }
    /// <summary>Asks the app to exit (the helper then replaces the exe and restarts it).</summary>
    public Action? RequestExit { get; set; }

    public Updater()
    {
        _timer = new System.Threading.Timer(async _ => await CheckAsync(false), null, TimeSpan.FromSeconds(20), TimeSpan.FromHours(6));
    }

    private static HttpClient CreateClient()
    {
        var h = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = true, MaxAutomaticRedirections = 5 }) { Timeout = TimeSpan.FromMinutes(5) };
        h.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("PirateChestCounter", AppInfo.VersionText));
        return h;
    }

    public static Version? ParseTag(string? tag) => PirateChest.Core.Updates.ReleaseFiles.ParseTag(tag);

    public sealed record Release(string Tag, Version Version, string ExeUrl, string SumsUrl);

    public static async Task<Release?> LatestAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{AppInfo.UpdateRepo}/releases/latest");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var resp = await Http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new LocalizedException($"на github.com/{AppInfo.UpdateRepo} пока нет релизов", $"github.com/{AppInfo.UpdateRepo} has no releases yet");
        }
        if (!resp.IsSuccessStatusCode)
        {
            throw new LocalizedException($"GitHub ответил {(int)resp.StatusCode}", $"GitHub answered {(int)resp.StatusCode}");
        }
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var tag = doc.RootElement.GetProperty("tag_name").GetString();
        var version = ParseTag(tag);
        string? exe = null, sums = null;
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var name = a.GetProperty("name").GetString();
            var url = a.GetProperty("browser_download_url").GetString();
            if (name == AssetName)
            {
                exe = url;
            }
            else if (name == SumsName)
            {
                sums = url;
            }
        }
        return version == null || exe == null || sums == null ? null : new Release(tag!, version, exe, sums);
    }

    private static bool IsGitHubUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == "https" && u.Host == "github.com";

    public static Version CurrentVersion => new(AppInfo.Version.Major, AppInfo.Version.Minor, Math.Max(0, AppInfo.Version.Build));

    public async Task CheckAsync(bool manual)
    {
        if (!await _gate.WaitAsync(0))
        {
            return;
        }
        try
        {
            State.CheckedUtc = DateTime.UtcNow;
            var rel = await LatestAsync();
            if (rel == null)
            {
                State.SetStatus("в последнем релизе нет файлов программы", "the latest release has no program files");
                return;
            }
            State.Latest = rel.Tag;
            if (rel.Version <= CurrentVersion)
            {
                State.Available = false;
                State.SetStatus("установлена последняя версия", "the latest version is installed");
                return;
            }
            State.Available = true;
            if (!AppPaths.RunningInstalled)
            {
                State.SetStatus($"доступна {rel.Tag} (автообновление работает только у установленной программы)", $"{rel.Tag} is available (auto-update works only for the installed program)");
                return;
            }
            if (!IsGitHubUrl(rel.ExeUrl) || !IsGitHubUrl(rel.SumsUrl))
            {
                State.SetStatus("ссылка на файл не ведёт на github.com — обновление пропущено", "the file link does not point to github.com, update skipped");
                return;
            }

            State.SetStatus($"скачиваю {rel.Tag}…", $"downloading {rel.Tag}…");
            var file = await DownloadVerifiedAsync(rel);
            State.SetStatus($"{rel.Tag} скачана и проверена (SHA256), устанавливаю…", $"{rel.Tag} downloaded and verified (SHA256), installing…");

            for (var i = 0; i < 120 && CanApplyNow?.Invoke() == false; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(10)); // let the current replay finish
            }
            Apply(file);
        }
        catch (Exception ex)
        {
            State.SetStatus("не удалось проверить обновления: " + ex.Message, "could not check for updates: " + (ex is LocalizedException le ? le.En : ex.Message));
            Log.Error("update check", ex);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<string> DownloadVerifiedAsync(Release rel)
    {
        Directory.CreateDirectory(AppPaths.UpdateDir);
        var sumsText = await Http.GetStringAsync(rel.SumsUrl);
        var expected = ExpectedHash(sumsText, AssetName)
            ?? throw new LocalizedException($"в {SumsName} нет строки для {AssetName}", $"{SumsName} has no line for {AssetName}");

        var target = Path.Combine(AppPaths.UpdateDir, AssetName);
        using (var resp = await Http.GetAsync(rel.ExeUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = File.Create(target);
            await resp.Content.CopyToAsync(fs);
        }
        var actual = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(target)));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(target);
            throw new LocalizedException("SHA256 скачанного файла не совпадает с SHA256SUMS.txt", "SHA256 of the downloaded file does not match SHA256SUMS.txt");
        }
        return target;
    }

    public static string? ExpectedHash(string sums, string name) => PirateChest.Core.Updates.ReleaseFiles.ExpectedHash(sums, name);

    /// <summary>Copies the verified exe next to the installed one and lets it replace us.</summary>
    private void Apply(string downloaded)
    {
        var helper = Path.Combine(AppPaths.InstallDir, HelperName);
        File.Copy(downloaded, helper, true);
        Detached.Start(helper, $"--apply-update {Environment.ProcessId}");
        Log.Info("update helper started, exiting");
        RequestExit?.Invoke();
    }

    /// <summary>Runs inside the new exe (PirateChestCounter.update.exe): waits for the old process, replaces it, restarts.</summary>
    public static int ApplyUpdateHelper(string pidText)
    {
        try
        {
            if (int.TryParse(pidText, out var pid))
            {
                try
                {
                    using var old = Process.GetProcessById(pid);
                    old.WaitForExit(30_000);
                }
                catch (ArgumentException)
                {
                    // already gone
                }
            }
            Exception? last = null;
            for (var i = 0; i < 40; i++)
            {
                try
                {
                    File.Copy(AppPaths.CurrentExe, AppPaths.InstalledExe, true);
                    last = null;
                    break;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    last = ex;
                    Thread.Sleep(500);
                }
            }
            if (last != null)
            {
                Log.Error("apply update: copy failed", last);
            }
            else
            {
                Log.Info($"updated to {AppInfo.VersionText}");
            }
            Detached.Start(AppPaths.InstalledExe, "--background --updated");
            return last == null ? 0 : 1;
        }
        catch (Exception ex)
        {
            Log.Error("apply update", ex);
            return 1;
        }
    }

    /// <summary>Removes the helper exe and download left from the previous update.</summary>
    public static void CleanupLeftovers()
    {
        Task.Run(async () =>
        {
            var helper = Path.Combine(AppPaths.InstallDir, HelperName);
            for (var i = 0; i < 30 && File.Exists(helper); i++)
            {
                try
                {
                    File.Delete(helper);
                }
                catch
                {
                    await Task.Delay(1000);
                }
            }
            try
            {
                if (Directory.Exists(AppPaths.UpdateDir))
                {
                    Directory.Delete(AppPaths.UpdateDir, true);
                }
            }
            catch
            {
                // best effort
            }
        });
    }

    public void Dispose() => _timer.Dispose();
}
