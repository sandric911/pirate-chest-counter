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
    /// <summary>Start of the replay being recorded right now (oldest live file of the last 45 min), if any.</summary>
    public DateTime? LiveSinceUtc { get; set; }
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
/// A replay is processed as soon as Fortnite has finished it: the header is no longer live and holds the decryption
/// key. That is noticed within about a second, so a match is counted a few seconds after it ends. A file whose
/// header cannot tell (unreadable, or the early read failed) waits until it has not been modified for 30 s.
/// Replays are opened read-only with FileShare.ReadWrite|Delete; nothing is ever written to them.
/// </summary>
public sealed class ReplayProcessor : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(30);
    /// <summary>How often a replay that is being recorded is looked at (file size and time only).</summary>
    private static readonly TimeSpan LivePoll = TimeSpan.FromMilliseconds(250);
    /// <summary>Its header is read when the file has changed, and at least this often in case the change went unnoticed.</summary>
    private static readonly TimeSpan LiveProbe = TimeSpan.FromSeconds(5);
    /// <summary>Fortnite writes to a replay every 20 s or so while recording; one that is quiet for longer is not watched closely.</summary>
    private static readonly TimeSpan ActiveRecording = TimeSpan.FromMinutes(2);

    private sealed class LiveFile
    {
        public long Size;
        public long MtimeTicks;
        public DateTime ProbedUtc;
    }

    private readonly Database _db;
    private readonly ChestAtlas _atlas;
    private readonly AutoResetEvent _wake = new(true);
    private readonly CancellationTokenSource _cts = new();
    private FileSystemWatcher? _watcher;
    private Thread? _thread;
    private readonly List<(string Path, string ReplayId)> _backupTodo = new();
    /// <summary>Replays being recorded right now, as the last scan saw them. Used by the processor thread only.</summary>
    private readonly Dictionary<string, LiveFile> _live = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Replays whose early read failed, with the size and time they had: they wait for the 30 s rule.</summary>
    private readonly Dictionary<string, (long Size, long MtimeTicks)> _earlyFailed = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Paths reported by the folder watcher since the processor thread last looked.</summary>
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _changed = new();
    private volatile bool _rescan = true;
    private bool _warm;
    private long _revision;

    public ProcessorStatus Status { get; } = new() { DemosDir = AppPaths.DemosDir };

    /// <summary>Raised after each match is stored. Arg: summary, whether it was part of a bulk backfill.</summary>
    public event Action<MatchSummary, bool>? MatchProcessed;
    /// <summary>Raised when a batch of more than one replay finished. Arg: counted matches in the batch.</summary>
    public event Action<int>? BatchFinished;

    public bool Busy => Status.Current != null || Status.Pending > 0;

    /// <summary>Grows every time the stored matches change, so the window can refresh at once instead of polling everything.</summary>
    public long Revision => Interlocked.Read(ref _revision);

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

    public void Wake()
    {
        _rescan = true;
        _wake.Set();
    }

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
            _watcher.Created += (_, _) => Wake();
            _watcher.Renamed += (_, _) => Wake();
            _watcher.Error += (_, _) => Wake();
            _watcher.Changed += (_, e) =>
            {
                _changed.Enqueue(e.FullPath);
                _wake.Set();
            };
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
                WarmUp();
            }
            catch (Exception ex)
            {
                Log.Error("processor loop", ex);
            }
            WaitForWork();
        }
    }

    /// <summary>
    /// The first replay read after the program starts takes several times longer than the following ones: the parser
    /// builds its readers and .NET compiles them. Reading one already counted replay ahead of time, in the background,
    /// moves that cost to the start of the program, so the first match of the evening is counted as fast as the rest.
    /// The result is thrown away.
    /// </summary>
    private void WarmUp()
    {
        if (_warm)
        {
            return;
        }
        _warm = true;
        try
        {
            var known = _db.Files().Where(f => f.Status is "done" or "skipped" && File.Exists(f.Path)).ToList();
            var pick = known.OrderByDescending(f => f.Status == "done").ThenByDescending(f => f.MtimeTicks).FirstOrDefault();
            if (pick == null)
            {
                return; // nothing counted yet: the first match pays for the warm-up itself
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            ReplayFiles.Parse(pick.Path);
            Log.Info($"warm-up: read {Path.GetFileName(pick.Path)} in {sw.Elapsed.TotalSeconds:F1} s");
        }
        catch (Exception ex)
        {
            Log.Info($"warm-up skipped: {ex.GetType().Name}: {ex.Message}");
        }
        GC.Collect();
    }

    /// <summary>
    /// Waits until the folder has to be scanned again: a replay that was being recorded is finished, something else
    /// changed in the folder, or the periodic rescan is due (a safety net for missed watcher events; unreadable
    /// files also need time to settle).
    /// </summary>
    private void WaitForWork()
    {
        var handles = new[] { _wake, _cts.Token.WaitHandle };
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(Status.Live > 0 ? 15 : 60);
        while (!_cts.IsCancellationRequested)
        {
            if (LiveReplayFinished())
            {
                return; // no pause here: the match has just ended and its number is wanted now
            }
            if (_rescan || ChangedOutsideLive())
            {
                Thread.Sleep(2000); // coalesce bursts of watcher events (a new replay, files being copied in)
                return;
            }
            var left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero)
            {
                return;
            }
            WaitHandle.WaitAny(handles, _live.Count > 0 && left > LivePoll ? LivePoll : left);
        }
    }

    /// <summary>True when the watcher reported a file other than the replays being recorded.</summary>
    private bool ChangedOutsideLive()
    {
        var outside = false;
        while (_changed.TryDequeue(out var path))
        {
            outside |= !_live.ContainsKey(path);
        }
        return outside;
    }

    /// <summary>
    /// Looks at the replays being recorded: size and time every time, the header when those changed (Fortnite has
    /// just written something) or were not looked at for a while. True as soon as one of them is finished or gone.
    /// </summary>
    private bool LiveReplayFinished()
    {
        foreach (var (path, seen) in _live)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists)
                {
                    return true;
                }
                var size = fi.Length;
                var mtime = fi.LastWriteTimeUtc.Ticks;
                var now = DateTime.UtcNow;
                if (size == seen.Size && mtime == seen.MtimeTicks && now - seen.ProbedUtc < LiveProbe)
                {
                    continue;
                }
                seen.Size = size;
                seen.MtimeTicks = mtime;
                seen.ProbedUtc = now;
                if (FinishedEarly(path, size, mtime))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"live check {Path.GetFileName(path)}", ex);
            }
        }
        return false;
    }

    /// <summary>
    /// True when Fortnite has finished this replay although it was written to less than 30 s ago: the header is
    /// not live and holds the decryption key. Not true again for a file whose early read has already failed.
    /// </summary>
    private bool FinishedEarly(string path, long size, long mtimeTicks) =>
        !(_earlyFailed.TryGetValue(path, out var failed) && failed == (size, mtimeTicks))
        && ReplayFiles.Probe(path) == ReplayHeaderState.Finished;

    /// <summary>
    /// Finds finished replays that still need counting. Already counted replays are recognised from the database
    /// (path + size + time) and never read again, unless the counting rules changed in an update.
    /// Backups stand in for replays Fortnite has already deleted.
    /// </summary>
    private List<string> Scan()
    {
        var queue = new List<string>();
        _rescan = false;
        _changed.Clear();
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
        _live.Clear();
        foreach (var gone in _earlyFailed.Keys.Where(p => !File.Exists(p)).ToList())
        {
            _earlyFailed.Remove(gone);
        }
        int live = 0, errors = 0;
        DateTime? newest = null, liveSince = null;
        void NoteLive(FileInfo f)
        {
            live++;
            var created = f.CreationTimeUtc;
            if (DateTime.UtcNow - created < TimeSpan.FromMinutes(45) && (liveSince == null || created < liveSince))
            {
                liveSince = created;
            }
            if (DateTime.UtcNow - f.LastWriteTimeUtc < ActiveRecording)
            {
                // Watch it closely until the next scan; the header was read just now (or is not worth reading yet).
                _live[f.FullName] = new LiveFile { Size = f.Length, MtimeTicks = f.LastWriteTimeUtc.Ticks, ProbedUtc = DateTime.UtcNow };
            }
        }

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

            // New or changed file. One that Fortnite has finished (header not live, key written) is taken at once;
            // any other is left alone until it has settled.
            if (DateTime.UtcNow - mtime < Settle && !FinishedEarly(path, fi.Length, mtime.Ticks))
            {
                NoteLive(fi);
                continue;
            }

            var peek = ReplayFiles.Peek(path);
            if (!peek.Valid)
            {
                if (DateTime.UtcNow - mtime < TimeSpan.FromMinutes(30))
                {
                    NoteLive(fi); // probably still being created
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
                    NoteLive(fi);
                    continue;
                }
            }
            // The very file that was counted has changed size since: it was read before Fortnite was done with it.
            var grown = row != null && row.Size != fi.Length && row.ReplayId != null
                && string.Equals(row.ReplayId, peek.ReplayId, StringComparison.OrdinalIgnoreCase);
            if (!grown && peek.ReplayId != null && states.TryGetValue(peek.ReplayId, out var known) && known.ParserVersion == AppInfo.ParserVersion)
            {
                // Same replay id already counted (copied/renamed file): never count twice.
                _db.UpsertFile(path, fi.Length, mtime.Ticks, peek.ReplayId, known.Status == "counted" ? "done" : "skipped");
                continue;
            }

            _db.UpsertFile(path, fi.Length, mtime.Ticks, peek.ReplayId, "pending", null, peek.Branch);
            queue.Add(path);
        }

        Status.Live = live;
        Status.LiveSinceUtc = liveSince;
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

        // The match that has just ended is read at normal priority: the player is waiting for its number.
        // A backlog stays in the background.
        Thread.CurrentThread.Priority = bulk ? ThreadPriority.BelowNormal : ThreadPriority.Normal;
        try
        {
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
                if (summary == null)
                {
                    continue;
                }
                if (summary.Counted)
                {
                    counted++;
                }
                if (Status.Pending == 0)
                {
                    Status.Current = null; // the window is told right now: it must not see "busy"
                }
                Interlocked.Increment(ref _revision);
                try
                {
                    MatchProcessed?.Invoke(summary, bulk);
                }
                catch (Exception ex)
                {
                    Log.Error("MatchProcessed handler", ex);
                }
                // The copy is made after the number is shown: it only matters later.
                if (summary.Counted && !IsBackup(path))
                {
                    EnsureBackup(path, summary.Meta.ReplayId);
                }
            }
        }
        finally
        {
            Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
        }

        Status.Current = null;
        Interlocked.Increment(ref _revision);
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
        var size = fi.Length;
        var mtime = fi.LastWriteTimeUtc;
        var build = _db.GetFile(path)?.Build;
        _warm = true;
        try
        {
            var trace = ReplayFiles.Parse(path);
            var after = new FileInfo(path);
            if (!after.Exists || after.Length != size || after.LastWriteTimeUtc != mtime)
            {
                // Fortnite (or a copy in progress) wrote to the file while it was being read: what was read may be
                // incomplete. Nothing is stored; the next scan looks at the file again.
                Log.Info($"{fi.Name}: changed while it was being read, will be read again");
                return null;
            }
            _earlyFailed.Remove(path);
            build = trace.Meta.Branch;
            if (trace.Meta.LengthMs < AppInfo.MinMatchSeconds * 1000L)
            {
                // Too short to be a match worth keeping (left on the start island, crashed, …): remember only that
                // this file was looked at, so it is not parsed again.
                _db.ForgetMatch(trace.Meta.ReplayId);
                _db.UpsertFile(path, size, mtime.Ticks, trace.Meta.ReplayId, "skipped", $"shorter than {AppInfo.MinMatchSeconds} s", build);
                Log.Info($"{fi.Name}: {trace.Meta.LengthMs / 1000} s, shorter than {AppInfo.MinMatchSeconds} s: not kept");
                return null;
            }
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
            _db.UpsertFile(path, size, mtime.Ticks, trace.Meta.ReplayId, summary.Counted ? "done" : "skipped", null, build);
            Log.Info($"{fi.Name}: {(summary.Counted ? $"mine={summary.Count.Mine} squad={summary.Count.MineAndSquad}" : "skipped: " + summary.SkipReason)}");
            return summary;
        }
        catch (Exception ex)
        {
            if (DateTime.UtcNow - mtime < Settle)
            {
                // Read right after the match and it failed: perhaps Fortnite was not quite done with the file.
                // Not an error yet; the file is read again once it has settled.
                Log.Info($"{fi.Name}: early read failed ({ex.GetType().Name}: {ex.Message}), will be read again once the file has settled");
                _earlyFailed[path] = (size, mtime.Ticks);
                _db.UpsertFile(path, size, mtime.Ticks, null, "live", null, build);
                return null;
            }
            Log.Error($"parse {fi.Name}", ex);
            _db.UpsertFile(path, size, mtime.Ticks, null, "error", $"{ex.GetType().Name}: {ex.Message}", build, true);
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
                output.Flush(flushToDisk: true);
            }
            // A copy that does not match the original is never kept as a backup; the next scan tries again.
            if (new FileInfo(tmp).Length != length)
            {
                File.Delete(tmp);
                Log.Error($"backup {Path.GetFileName(src)}: copy size differs from the original, will retry");
                return;
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
