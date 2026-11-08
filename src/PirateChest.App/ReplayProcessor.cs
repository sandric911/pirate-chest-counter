using PirateChest.Core.Counting;
using PirateChest.ReplayReader;

namespace PirateChest.App;

public sealed class ProcessorStatus
{
    public bool DemosFound { get; set; }
    public string DemosDir { get; set; } = "";
    public int ReplayFiles { get; set; }
    public int Pending { get; set; }
    public int Live { get; set; }
    public int Errors { get; set; }
    public string? Current { get; set; }
    public int BatchTotal { get; set; }
    public int BatchDone { get; set; }
    public DateTime? NewestReplayUtc { get; set; }
    public int Backups { get; set; }
    public long BackupBytes { get; set; }
    public DateTime? LastScanUtc { get; set; }
}

/// <summary>
/// Watches the Demos folder and counts finished replays one by one in the background.
/// A replay is processed only when it is finished: header not live, file not modified for 30 s.
/// Replays are opened read-only with FileShare.ReadWrite|Delete; nothing is ever written to them.
/// </summary>
public sealed class ReplayProcessor : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(30);

    private readonly Database _db;
    private readonly ChestAtlas _atlas;
    private readonly AutoResetEvent _wake = new(true);
    private readonly CancellationTokenSource _cts = new();
    private FileSystemWatcher? _watcher;
    private Thread? _thread;
    private readonly List<(string Path, string ReplayId)> _backupTodo = new();

    public ProcessorStatus Status { get; } = new() { DemosDir = AppPaths.DemosDir };

    /// <summary>Raised after each match is stored. Arg: summary, whether it was part of a bulk backfill.</summary>
    public event Action<MatchSummary, bool>? MatchProcessed;
    /// <summary>Raised when a batch of more than one replay finished. Arg: counted matches in the batch.</summary>
    public event Action<int>? BatchFinished;

    public bool Busy => Status.Current != null || Status.Pending > 0;

    public ReplayProcessor(Database db, ChestAtlas atlas)
    {
        _db = db;
        _atlas = atlas;
    }

    public void Start()
    {
        TryWatch();
        _thread = new Thread(Loop) { IsBackground = true, Name = "replay-processor", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    public void Wake() => _wake.Set();

    public void RecountAll()
    {
        _db.RequeueAll();
        Wake();
    }

    private void TryWatch()
    {
        try
        {
            if (_watcher != null || !Directory.Exists(AppPaths.DemosDir))
            {
                return;
            }
            _watcher = new FileSystemWatcher(AppPaths.DemosDir, "*.replay")
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Created += (_, _) => _wake.Set();
            _watcher.Changed += (_, _) => _wake.Set();
            _watcher.Renamed += (_, _) => _wake.Set();
        }
        catch (Exception ex)
        {
            Log.Error("watcher", ex);
        }
    }

    private void Loop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                TryWatch();
                var queue = Scan();
                if (queue.Count > 0)
                {
                    ProcessBatch(queue);
                    continue; // rescan right away: more files may have settled meanwhile
                }
                MakeBackups();
            }
            catch (Exception ex)
            {
                Log.Error("processor loop", ex);
            }
            // Periodic rescan as a safety net for missed watcher events; files also need time to settle.
            WaitHandle.WaitAny(new[] { _wake, _cts.Token.WaitHandle }, TimeSpan.FromSeconds(Status.Live > 0 ? 15 : 60));
            if (!_cts.IsCancellationRequested)
            {
                Thread.Sleep(2000); // coalesce bursts of watcher events
            }
        }
    }

    /// <summary>
    /// Finds finished replays that still need counting. Already counted replays are recognised from the database
    /// (path + size + time) and never read again, unless the counting rules changed in an update.
    /// Backups stand in for replays Fortnite has already deleted.
    /// </summary>
    private List<string> Scan()
    {
        var queue = new List<string>();
        Status.LastScanUtc = DateTime.UtcNow;
        Status.DemosFound = Directory.Exists(AppPaths.DemosDir);
        var demos = Status.DemosFound ? Directory.GetFiles(AppPaths.DemosDir, "*.replay") : Array.Empty<string>();
        Status.ReplayFiles = demos.Length;

        var rows = _db.Files().ToDictionary(r => r.Path, StringComparer.OrdinalIgnoreCase);
        var states = _db.MatchStates();
        var liveIds = demos.Select(p => rows.TryGetValue(p, out var r) ? r.ReplayId : null)
            .OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var backups = Directory.Exists(AppPaths.BackupDir) ? Directory.GetFiles(AppPaths.BackupDir, "*.replay") : Array.Empty<string>();
        Status.Backups = backups.Length;
        Status.BackupBytes = backups.Sum(b => new FileInfo(b).Length);
        var files = demos.Concat(backups.Where(b => !liveIds.Contains(Path.GetFileNameWithoutExtension(b)))).ToArray();

        _db.ForgetMissingFiles(files.ToHashSet(StringComparer.OrdinalIgnoreCase));
        _backupTodo.Clear();
        int live = 0, errors = 0;
        DateTime? newest = null;

        foreach (var path in files.OrderBy(f => f))
        {
            var fi = new FileInfo(path);
            if (!fi.Exists)
            {
                continue;
            }
            var mtime = fi.LastWriteTimeUtc;
            if (newest == null || mtime > newest)
            {
                newest = mtime;
            }

            rows.TryGetValue(path, out var row);
            var unchanged = row != null && row.Size == fi.Length && row.MtimeTicks == mtime.Ticks;

            if (unchanged && row!.Status is "done" or "skipped")
            {
                if (row.ReplayId != null && states.TryGetValue(row.ReplayId, out var st))
                {
                    if (st.ParserVersion != AppInfo.ParserVersion)
                    {
                        queue.Add(path); // the counting rules changed in an update
                    }
                    else if (st.Status == "counted" && !IsBackup(path) && !File.Exists(BackupPathOf(row.ReplayId)))
                    {
                        _backupTodo.Add((path, row.ReplayId));
                    }
                }
                continue;
            }
            if (unchanged && row!.Status == "error")
            {
                // Failed replays are retried automatically after the program is updated.
                if (row.AppVersion != AppInfo.VersionText)
                {
                    queue.Add(path);
                }
                else
                {
                    errors++;
                }
                continue;
            }
            if (unchanged && row!.Status == "pending")
            {
                queue.Add(path);
                continue;
            }

            // New or changed file: only touch it once it has settled.
            if (DateTime.UtcNow - mtime < Settle)
            {
                live++;
                continue;
            }

            var peek = ReplayFiles.Peek(path);
            if (!peek.Valid)
            {
                if (DateTime.UtcNow - mtime < TimeSpan.FromMinutes(30))
                {
                    live++; // probably still being created
                    continue;
                }
                _db.UpsertFile(path, fi.Length, mtime.Ticks, null, "error", "Не удалось прочитать заголовок: " + peek.Error, null, true);
                errors++;
                continue;
            }
            if (peek.IsLive)
            {
                // A crashed game can leave a replay "live" forever; give it a long grace period, then try anyway.
                if (DateTime.UtcNow - mtime < TimeSpan.FromHours(3))
                {
                    _db.UpsertFile(path, fi.Length, mtime.Ticks, peek.ReplayId, "live");
                    live++;
                    continue;
                }
            }
            if (peek.ReplayId != null && states.TryGetValue(peek.ReplayId, out var known) && known.ParserVersion == AppInfo.ParserVersion)
            {
                // Same replay id already counted (copied/renamed file): never count twice.
                _db.UpsertFile(path, fi.Length, mtime.Ticks, peek.ReplayId, known.Status == "counted" ? "done" : "skipped");
                continue;
            }

            _db.UpsertFile(path, fi.Length, mtime.Ticks, peek.ReplayId, "pending", null, peek.Branch);
            queue.Add(path);
        }

        Status.Live = live;
        Status.Errors = errors;
        Status.Pending = queue.Count;
        Status.NewestReplayUtc = newest;
        return queue;
    }

    private void ProcessBatch(List<string> queue)
    {
        var bulk = queue.Count > 1;
        Status.BatchTotal = queue.Count;
        Status.BatchDone = 0;
        var counted = 0;

        foreach (var path in queue)
        {
            if (_cts.IsCancellationRequested)
            {
                return;
            }
            Status.Current = Path.GetFileName(path);
            var summary = ProcessOne(path);
            Status.BatchDone++;
            Status.Pending = Math.Max(0, queue.Count - Status.BatchDone);
            if (summary != null)
            {
                if (summary.Counted)
                {
                    counted++;
                }
                try
                {
                    MatchProcessed?.Invoke(summary, bulk);
                }
                catch (Exception ex)
                {
                    Log.Error("MatchProcessed handler", ex);
                }
            }
        }

        Status.Current = null;
        _db.SaveAtlas(_atlas);
        if (bulk)
        {
            try
            {
                BatchFinished?.Invoke(counted);
            }
            catch (Exception ex)
            {
                Log.Error("BatchFinished handler", ex);
            }
        }
        GC.Collect();
    }

    private MatchSummary? ProcessOne(string path)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists)
        {
            return null;
        }
        var build = _db.GetFile(path)?.Build;
        try
        {
            var trace = ReplayFiles.Parse(path);
            build = trace.Meta.Branch;
            lock (_atlas)
            {
                // Learn chest positions once per replay (a recount must not double the samples).
                if (_db.MatchState(trace.Meta.ReplayId) == null)
                {
                    _atlas.Learn(trace);
                }
            }
            var options = new CountingOptions
            {
                HeuristicRadiusMeters = _db.GetDouble("heuristic_radius_m", 15),
                HeuristicWindowSeconds = _db.GetDouble("heuristic_window_s", 2),
            };
            MatchSummary summary;
            lock (_atlas)
            {
                summary = new MatchAnalyzer(options: options, locator: _atlas).Analyze(trace);
            }
            _db.SaveMatch(IsBackup(path) ? FileNameOf(trace.Meta) : fi.Name, summary);
            if (summary.Counted && !IsBackup(path))
            {
                EnsureBackup(path, trace.Meta.ReplayId);
            }
            _db.UpsertFile(path, fi.Length, fi.LastWriteTimeUtc.Ticks, trace.Meta.ReplayId, summary.Counted ? "done" : "skipped", null, build);
            Log.Info($"{fi.Name}: {(summary.Counted ? $"mine={summary.Count.Mine} squad={summary.Count.MineAndSquad}" : "skipped: " + summary.SkipReason)}");
            return summary;
        }
        catch (Exception ex)
        {
            Log.Error($"parse {fi.Name}", ex);
            _db.UpsertFile(path, fi.Length, fi.LastWriteTimeUtc.Ticks, null, "error", $"{ex.GetType().Name}: {ex.Message}", build, true);
            return null;
        }
    }

    private static string FileNameOf(PirateChest.Core.Trace.ReplayMeta meta) =>
        $"UnsavedReplay-{meta.TimestampUtc.ToLocalTime():yyyy.MM.dd-HH.mm.ss}.replay";

    private static bool IsBackup(string path) =>
        path.StartsWith(AppPaths.BackupDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string BackupPathOf(string replayId)
    {
        var safe = new string(replayId.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-').ToArray());
        return Path.Combine(AppPaths.BackupDir, safe + ".replay");
    }

    /// <summary>Copies finished, counted replays that have no backup yet (first run / after an update).</summary>
    private void MakeBackups()
    {
        foreach (var (path, id) in _backupTodo)
        {
            if (_cts.IsCancellationRequested)
            {
                return;
            }
            EnsureBackup(path, id);
        }
        _backupTodo.Clear();
    }

    /// <summary>
    /// Copies a finished replay into the program's folder. The original is opened read-only with
    /// FileShare.ReadWrite|Delete, so Fortnite can still delete or rotate it; the copy is written under a temp
    /// name and renamed, so a half-copied file is never mistaken for a backup.
    /// </summary>
    private static void EnsureBackup(string src, string replayId)
    {
        try
        {
            var dest = BackupPathOf(replayId);
            var length = new FileInfo(src).Length;
            if (File.Exists(dest) && new FileInfo(dest).Length == length)
            {
                return;
            }
            Directory.CreateDirectory(AppPaths.BackupDir);
            var tmp = dest + ".tmp";
            using (var input = new FileStream(src, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 20, FileOptions.SequentialScan))
            using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                input.CopyTo(output);
            }
            File.Move(tmp, dest, true);
            File.SetLastWriteTimeUtc(dest, File.GetLastWriteTimeUtc(src));
        }
        catch (Exception ex)
        {
            Log.Error($"backup {Path.GetFileName(src)}", ex);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _watcher?.Dispose();
        _thread?.Join(TimeSpan.FromSeconds(5));
    }
}
