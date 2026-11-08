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

    /// <summary>Finds finished replays that still need counting.</summary>
    private List<string> Scan()
    {
        var queue = new List<string>();
        Status.LastScanUtc = DateTime.UtcNow;
        Status.DemosFound = Directory.Exists(AppPaths.DemosDir);
        if (!Status.DemosFound)
        {
            Status.ReplayFiles = 0;
            return queue;
        }

        var files = Directory.GetFiles(AppPaths.DemosDir, "*.replay");
        Status.ReplayFiles = files.Length;
        _db.ForgetMissingFiles(files.ToHashSet(StringComparer.OrdinalIgnoreCase));
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

            var row = _db.GetFile(path);
            var unchanged = row != null && row.Size == fi.Length && row.MtimeTicks == mtime.Ticks;

            if (unchanged && row!.Status is "done" or "skipped")
            {
                // Recount when the counting rules changed since this replay was counted.
                if (row.ReplayId != null && _db.MatchState(row.ReplayId) is { } st && st.ParserVersion != AppInfo.ParserVersion)
                {
                    queue.Add(path);
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
            if (peek.ReplayId != null && _db.MatchState(peek.ReplayId) is { } known && known.ParserVersion == AppInfo.ParserVersion)
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
            _db.SaveMatch(fi.Name, summary);
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

    public void Dispose()
    {
        _cts.Cancel();
        _watcher?.Dispose();
        _thread?.Join(TimeSpan.FromSeconds(5));
    }
}
