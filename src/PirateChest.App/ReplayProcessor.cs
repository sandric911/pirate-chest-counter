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
/// A replay is counted as soon as Fortnite has finished it. While it is recorded, its header is watched (the folder
/// watcher, plus a look at the file's size and time ten times a second); the moment the header is final the file is
/// read once, whole, and those very bytes are first checked to be a complete replay (<see cref="ReplayLayout.Complete"/>)
/// and then counted. So the number is on screen about half a second after Fortnite has written the replay, and it is
/// never a number from a half-written file. A file that is not complete is not counted early: it waits until it
/// changes, or, failing that, until it has not been modified for 30 s, the rule every replay went by before.
/// Replays are opened read-only with FileShare.ReadWrite|Delete; nothing is ever written to them.
/// </summary>
public sealed class ReplayProcessor : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(30);
    /// <summary>How often a replay that is being recorded is looked at (file size and time only, the file is not opened).</summary>
    private static readonly TimeSpan LivePoll = TimeSpan.FromMilliseconds(100);
    /// <summary>Its header is read when the file has changed, and at least this often in case the change went unnoticed.</summary>
    private static readonly TimeSpan LiveProbe = TimeSpan.FromSeconds(2);
    /// <summary>Fortnite writes to a replay every 20 s or so while recording; one that is quiet for longer is not watched closely.</summary>
    private static readonly TimeSpan ActiveRecording = TimeSpan.FromMinutes(2);
    /// <summary>
    /// A replay with a final header that is not complete is given this long to receive the rest; if it was seen with
    /// the same bytes that long ago, that is all there will be and it is counted.
    /// </summary>
    private static readonly TimeSpan IncompleteGrace = TimeSpan.FromSeconds(3);
    /// <summary>Changes in the folder that are not the end of a match (a new replay, files being copied in) are gathered for this long.</summary>
    private static readonly TimeSpan Coalesce = TimeSpan.FromSeconds(2);

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
    /// <summary>
    /// Finished replays that could not be counted early (not complete, or the read failed), with the size and time they
    /// had: they are left alone until they change or the given moment comes. For one that was not complete, the hash
    /// of the bytes that were read, to tell later whether the file has really stayed as it was.
    /// Used by the processor thread only.
    /// </summary>
    private readonly Dictionary<string, (long Size, long MtimeTicks, DateTime RetryUtc, string? Hash)> _deferred = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>
    /// The replay that was just read whole and found complete, kept until it is counted a moment later, so the file
    /// is read once. One at a time: a replay is 10–20 MB.
    /// </summary>
    private (string Path, long Size, long MtimeTicks, byte[] Bytes)? _snapshot;
    /// <summary>Paths reported by the folder watcher since the processor thread last looked.</summary>
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _changed = new();
    private volatile bool _rescan = true;
    private bool _warm;
    private long _revision;
    private TaskCompletionSource _revisionChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ProcessorStatus Status { get; } = new() { DemosDir = AppPaths.DemosDir };

    /// <summary>Raised after each match is stored. Arg: summary, whether it was part of a bulk backfill.</summary>
    public event Action<MatchSummary, bool>? MatchProcessed;
    /// <summary>Raised when a batch of more than one replay finished. Arg: counted matches in the batch.</summary>
    public event Action<int>? BatchFinished;

    public bool Busy => Status.Current != null || Status.Pending > 0;

    /// <summary>Grows every time the stored matches change, so the window can refresh at once instead of polling everything.</summary>
    public long Revision => Interlocked.Read(ref _revision);

    /// <summary>
    /// Returns the revision as soon as it differs from <paramref name="since"/>, or after <paramref name="timeout"/>
    /// at the latest. The window keeps one such request open, so it learns of a counted match the moment it is stored.
    /// </summary>
    public async Task<long> WaitForRevisionAsync(long? since, TimeSpan timeout, CancellationToken cancel)
    {
        var changed = Volatile.Read(ref _revisionChanged).Task; // taken before the comparison: a bump in between is not lost
        if (since == Revision && !_cts.IsCancellationRequested)
        {
            try
            {
                await changed.WaitAsync(timeout, cancel);
            }
            catch (TimeoutException)
            {
                // nothing changed: the caller asks again
            }
        }
        return Revision;
    }

    private void BumpRevision()
    {
        Interlocked.Increment(ref _revision);
        Interlocked.Exchange(ref _revisionChanged, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
    }

    public ReplayProcessor(Database db, ChestAtlas atlas)
    {
        _db = db;
        _atlas = atlas;
        ReplayFiles.ReadAllPawnFields = _db.Get(ReadAllPawnFieldsKey) == AppInfo.VersionText;
    }

    public void Start()
    {
        TryWatch();
        // Watching costs next to nothing and must not wait behind the game for a free core, so the thread runs at
        // normal priority; the heavy work it does (a backlog, backup copies, the warm-up) lowers it for itself.
        _thread = new Thread(Loop) { IsBackground = true, Name = "replay-processor" };
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
        string? finished = null;
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                TryWatch();
                if (finished is { } path)
                {
                    // The match has just ended: only its replay is looked at and counted. The folder is scanned
                    // right after, when the number is already on screen.
                    finished = null;
                    _live.Remove(path);
                    _rescan = true;
                    if (ExamineOne(path))
                    {
                        ProcessBatch(new List<string> { path });
                    }
                    continue;
                }
                var queue = Scan();
                if (queue.Count > 0)
                {
                    ProcessBatch(queue);
                    continue; // rescan right away: more files may have settled meanwhile
                }
                InBackground(() =>
                {
                    MakeBackups();
                    WarmUp();
                });
            }
            catch (Exception ex)
            {
                Log.Error("processor loop", ex);
            }
            finished = WaitForWork();
        }
    }

    /// <summary>Work nobody is waiting for runs below the priority of everything else on the PC, the game first of all.</summary>
    private static void InBackground(Action work)
    {
        var thread = Thread.CurrentThread;
        var before = thread.Priority;
        thread.Priority = ThreadPriority.BelowNormal;
        try
        {
            work();
        }
        finally
        {
            thread.Priority = before;
        }
    }

    /// <summary>
    /// Once after the program starts, in the background, one already counted replay is read and the result thrown
    /// away. It does two things. The first replay read takes several times longer than the following ones (the parser
    /// builds its readers, .NET compiles them); this moves that cost to the start of the program, so the first match
    /// of the evening is counted as fast as the rest. And the replay is read both ways, with only the pawn fields the
    /// counting uses and with all of them: if the results ever differ (a Fortnite update the fast reading does not
    /// agree with), the program goes back to reading everything and counts every match again.
    /// </summary>
    private void WarmUp()
    {
        if (_warm)
        {
            return;
        }
        try
        {
            var known = _db.Files().Where(f => f.Status is "done" or "skipped" && File.Exists(f.Path)).ToList();
            var pick = known.OrderByDescending(f => f.Status == "done").ThenByDescending(f => f.MtimeTicks).FirstOrDefault();
            if (pick == null)
            {
                return; // nothing counted yet: the first match pays for the warm-up itself, and the check follows it
            }
            _warm = true; // once per run, also when a backlog has just been counted: the check below is wanted either way
            var name = Path.GetFileName(pick.Path);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var bytes = ReplayFiles.ReadAll(pick.Path);
            ReplayFiles.Inspect(bytes);
            if (ReplayFiles.ReadAllPawnFields)
            {
                ReplayFiles.Parse(bytes);
            }
            else if (!ReplayFiles.SameWithAllPawnFields(bytes))
            {
                Log.Error($"{name}: reading only the pawn fields that are used gave a different result than reading all of them. All fields are read from now on, and every match is counted again.");
                ReplayFiles.ReadAllPawnFields = true;
                _db.Set(ReadAllPawnFieldsKey, AppInfo.VersionText);
                RecountAll();
            }
            Log.Info($"warm-up: read {name} in {sw.Elapsed.TotalSeconds:F1} s");
        }
        catch (Exception ex)
        {
            Log.Info($"warm-up skipped: {ex.GetType().Name}: {ex.Message}");
        }
        GC.Collect();
    }

    /// <summary>Setting that holds the program version in which the fast pawn reading was found to differ; it is tried again after an update.</summary>
    private const string ReadAllPawnFieldsKey = "read_all_pawn_fields";

    /// <summary>
    /// Waits until there is something to do. Returns the path of a replay that Fortnite has just finished, to be
    /// counted at once; or null when the folder has to be scanned: something else changed in it, or the periodic
    /// rescan is due (a safety net for missed watcher events; unreadable files also need time to settle).
    /// </summary>
    private string? WaitForWork()
    {
        var handles = new[] { _wake, _cts.Token.WaitHandle };
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(Status.Live > 0 ? 15 : 60);
        var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!_cts.IsCancellationRequested)
        {
            // What the watcher saw: a replay being recorded has its header read at once; anything else means a scan,
            // a little later (a burst of events is one scan), and the replays being recorded stay watched meanwhile.
            reported.Clear();
            while (_changed.TryDequeue(out var path))
            {
                if (!reported.Add(path) || _live.ContainsKey(path))
                {
                    continue;
                }
                _rescan = true;
            }
            if (LiveReplayFinished(reported) is { } finished)
            {
                return finished; // no pause here: the match has just ended and its number is wanted now
            }
            var now = DateTime.UtcNow;
            if (_rescan && deadline > now + Coalesce)
            {
                deadline = now + Coalesce;
            }
            var left = deadline - now;
            if (left <= TimeSpan.Zero)
            {
                return null;
            }
            WaitHandle.WaitAny(handles, _live.Count > 0 && left > LivePoll ? LivePoll : left);
        }
        return null;
    }

    /// <summary>
    /// Looks at the replays being recorded: size and time every time, the header when those changed or the watcher
    /// named the file (Fortnite has just written something), or when it was not looked at for a while. Returns the
    /// first one that Fortnite has finished.
    /// </summary>
    private string? LiveReplayFinished(HashSet<string> reported)
    {
        foreach (var (path, seen) in _live)
        {
            try
            {
                var fi = new FileInfo(path);
                if (!fi.Exists)
                {
                    _rescan = true; // deleted or renamed: the scan sorts it out
                    continue;
                }
                var size = fi.Length;
                var mtime = fi.LastWriteTimeUtc.Ticks;
                var now = DateTime.UtcNow;
                if (size == seen.Size && mtime == seen.MtimeTicks && !reported.Contains(path) && now - seen.ProbedUtc < LiveProbe)
                {
                    continue;
                }
                seen.Size = size;
                seen.MtimeTicks = mtime;
                seen.ProbedUtc = now;
                if (FinishedEarly(path, size, mtime))
                {
                    return path;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"live check {Path.GetFileName(path)}", ex);
            }
        }
        return null;
    }

    /// <summary>
    /// True when Fortnite has finished this replay although it was written to less than 30 s ago: the header is
    /// not live and holds the decryption key. Not true for a file that was put off (<see cref="_deferred"/>) and has
    /// neither changed since nor reached its time.
    /// </summary>
    private bool FinishedEarly(string path, long size, long mtimeTicks) =>
        !(_deferred.TryGetValue(path, out var d) && d.Size == size && d.MtimeTicks == mtimeTicks && DateTime.UtcNow < d.RetryUtc)
        && ReplayFiles.Probe(path) == ReplayHeaderState.Finished;

    /// <summary>
    /// Reads a replay that Fortnite has just finished, whole, and tells whether these bytes can be counted now: they
    /// are a complete replay, or one with a final header that was read with these same bytes at least
    /// <see cref="IncompleteGrace"/> ago. If not, the file is put off: until it changes, or until the grace is over.
    /// </summary>
    private byte[]? ReadIfCountable(string path, long size, DateTime mtime, out ReplayLayout layout)
    {
        byte[] bytes;
        try
        {
            bytes = ReplayFiles.ReadAll(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Locked or cut short at this very moment: it stays watched and is looked at again shortly.
            layout = new ReplayLayout { State = ReplayHeaderState.Unreadable, Problem = ex.Message };
            return null;
        }
        layout = ReplayFiles.Inspect(bytes);
        var after = new FileInfo(path);
        if (!after.Exists || after.Length != size || after.LastWriteTimeUtc != mtime || bytes.Length != size)
        {
            return null; // written to while it was read: not put off, the change itself brings it back
        }
        if (layout.Complete)
        {
            return bytes;
        }
        var name = Path.GetFileName(path);
        var now = DateTime.UtcNow;
        if (layout.State != ReplayHeaderState.Finished)
        {
            _deferred[path] = (size, mtime.Ticks, mtime + Settle, null);
            return null;
        }
        // A final header on a file that is not all there: seen when a replay is being copied into the folder, and
        // what a replay would look like if Fortnite ever wrote its header before the rest. The file's time is not
        // trusted to tell that nothing is being written (it can lag behind an open file): the bytes are compared.
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
        if (_deferred.TryGetValue(path, out var seen) && seen.Hash == hash)
        {
            if (now >= seen.RetryUtc)
            {
                Log.Info($"{name}: counted although {layout.Problem}; the file has not changed for {IncompleteGrace.TotalSeconds:F0} s");
                return bytes;
            }
            _deferred[path] = (size, mtime.Ticks, seen.RetryUtc, hash);
            return null;
        }
        Log.Info($"{name}: not counted yet, {layout.Problem}");
        _deferred[path] = (size, mtime.Ticks, now + IncompleteGrace, hash);
        return null;
    }

    private byte[]? TakeSnapshot(string path, long size, long mtimeTicks)
    {
        var kept = _snapshot;
        _snapshot = null;
        return kept is { } k && k.Size == size && k.MtimeTicks == mtimeTicks && string.Equals(k.Path, path, StringComparison.OrdinalIgnoreCase)
            ? k.Bytes
            : null;
    }

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
        foreach (var gone in _deferred.Keys.Where(p => !File.Exists(p)).ToList())
        {
            _deferred.Remove(gone);
        }
        int live = 0, errors = 0;
        DateTime? newest = null, liveSince = null;

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
            switch (Examine(fi, row, id => states.TryGetValue(id, out var st) ? st : null))
            {
                case Verdict.Count:
                    queue.Add(path);
                    break;
                case Verdict.Error:
                    errors++;
                    break;
                case Verdict.Live:
                    live++;
                    var created = fi.CreationTimeUtc;
                    if (DateTime.UtcNow - created < TimeSpan.FromMinutes(45) && (liveSince == null || created < liveSince))
                    {
                        liveSince = created;
                    }
                    if (DateTime.UtcNow - mtime < ActiveRecording)
                    {
                        // Watch it closely until the next scan; the header was read just now (or is not worth reading yet).
                        _live[fi.FullName] = new LiveFile { Size = fi.Length, MtimeTicks = mtime.Ticks, ProbedUtc = DateTime.UtcNow };
                    }
                    break;
            }
        }

        Status.Live = live;
        Status.LiveSinceUtc = liveSince;
        Status.Errors = errors;
        Status.Pending = queue.Count;
        Status.NewestReplayUtc = newest;
        return queue;
    }

    /// <summary>
    /// Decides about the one replay that Fortnite has just finished, by the same rules as <see cref="Scan"/> but
    /// without going through the folder. True when it is to be counted now.
    /// </summary>
    private bool ExamineOne(string path)
    {
        var fi = new FileInfo(path);
        if (!fi.Exists || Examine(fi, _db.GetFile(path), id => _db.MatchState(id)) != Verdict.Count)
        {
            return false;
        }
        Status.Pending = 1;
        return true;
    }

    private enum Verdict
    {
        /// <summary>Nothing to do: counted already, or deliberately not counted.</summary>
        Settled,
        /// <summary>Still being recorded, or not ready to be counted yet.</summary>
        Live,
        /// <summary>To be counted now.</summary>
        Count,
        /// <summary>Could not be read; it is retried after the program is updated.</summary>
        Error,
    }

    /// <summary>What to do with one replay file, given what the database knows about it.</summary>
    private Verdict Examine(FileInfo fi, FileRow? row, Func<string, (string Status, string ParserVersion)?> stateOf)
    {
        var path = fi.FullName;
        var mtime = fi.LastWriteTimeUtc;
        var unchanged = row != null && row.Size == fi.Length && row.MtimeTicks == mtime.Ticks;

        if (unchanged && row!.Status is "done" or "skipped")
        {
            if (row.ReplayId != null && stateOf(row.ReplayId) is { } st)
            {
                if (st.ParserVersion != AppInfo.ParserVersion)
                {
                    return Verdict.Count; // the counting rules changed in an update
                }
                if (st.Status == "counted" && !IsBackup(path) && !File.Exists(BackupPathOf(row.ReplayId)))
                {
                    _backupTodo.Add((path, row.ReplayId));
                }
            }
            return Verdict.Settled;
        }
        if (unchanged && row!.Status == "error")
        {
            // Failed replays are retried automatically after the program is updated.
            return row.AppVersion != AppInfo.VersionText ? Verdict.Count : Verdict.Error;
        }
        if (unchanged && row!.Status == "pending")
        {
            return Verdict.Count;
        }

        // New or changed file.
        string? replayId, branch;
        if (DateTime.UtcNow - mtime < Settle)
        {
            // Written to a moment ago. One that Fortnite has finished (header not live, key written) is read whole
            // right now, and taken if those bytes are a complete replay; any other is left alone until it has settled.
            if (!FinishedEarly(path, fi.Length, mtime.Ticks))
            {
                return Verdict.Live;
            }
            var bytes = ReadIfCountable(path, fi.Length, mtime, out var layout);
            if (bytes == null)
            {
                return Verdict.Live;
            }
            _snapshot = (path, fi.Length, mtime.Ticks, bytes);
            replayId = layout.ReplayId;
            branch = layout.Branch;
        }
        else
        {
            var peek = ReplayFiles.Peek(path);
            if (!peek.Valid)
            {
                if (DateTime.UtcNow - mtime < TimeSpan.FromMinutes(30))
                {
                    return Verdict.Live; // probably still being created
                }
                _db.UpsertFile(path, fi.Length, mtime.Ticks, null, "error", "Не удалось прочитать заголовок: " + peek.Error, null, true);
                return Verdict.Error;
            }
            // A crashed game can leave a replay "live" forever; give it a long grace period, then try anyway.
            if (peek.IsLive && DateTime.UtcNow - mtime < TimeSpan.FromHours(3))
            {
                _db.UpsertFile(path, fi.Length, mtime.Ticks, peek.ReplayId, "live");
                return Verdict.Live;
            }
            replayId = peek.ReplayId;
            branch = peek.Branch;
        }

        // This very file is already known as this replay, and it is not as the database remembers it: whatever was
        // read before, it is not what is there now, so it is counted (again). The row's state does not matter: an
        // earlier attempt at this may have been interrupted and left it "pending" or "live".
        // (Never expected of a replay Fortnite has finished; this is what puts the number right if it happens.)
        var recount = row?.ReplayId != null && string.Equals(row.ReplayId, replayId, StringComparison.OrdinalIgnoreCase);
        if (recount)
        {
            if (row!.Status is "done" or "skipped")
            {
                Log.Info($"{fi.Name}: changed after it was counted ({row.Size} → {fi.Length} bytes), counting it again");
            }
        }
        else if (replayId != null && stateOf(replayId) is { } known && known.ParserVersion == AppInfo.ParserVersion)
        {
            // Same replay id already counted (copied/renamed file): never count twice.
            _snapshot = null;
            _db.UpsertFile(path, fi.Length, mtime.Ticks, replayId, known.Status == "counted" ? "done" : "skipped");
            return Verdict.Settled;
        }

        _db.UpsertFile(path, fi.Length, mtime.Ticks, replayId, "pending", null, branch);
        return Verdict.Count;
    }

    private void ProcessBatch(List<string> queue)
    {
        var bulk = queue.Count > 1;
        Status.BatchTotal = queue.Count;
        Status.BatchDone = 0;
        Status.Pending = queue.Count;
        var counted = 0;
        var announced = DateTime.MinValue;
        var thread = Thread.CurrentThread;

        try
        {
            foreach (var path in queue)
            {
                if (_cts.IsCancellationRequested)
                {
                    return;
                }
                Status.Current = Path.GetFileName(path);
                // The match that has just ended is read ahead of everything else on the PC, for the half second it
                // takes: the player is looking at the counter, and the game is on a loading screen. One older replay
                // is read at normal priority, a backlog in the background.
                thread.Priority = bulk ? ThreadPriority.BelowNormal
                    : JustWritten(path) ? ThreadPriority.AboveNormal
                    : ThreadPriority.Normal;
                var summary = ProcessOne(path);
                thread.Priority = bulk ? ThreadPriority.BelowNormal : ThreadPriority.Normal;
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
                // One match is announced at once; a backlog at most once a second (and once more when it is done).
                if (!bulk || DateTime.UtcNow - announced >= TimeSpan.FromSeconds(1))
                {
                    announced = DateTime.UtcNow;
                    BumpRevision();
                }
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
                    InBackground(() => EnsureBackup(path, summary.Meta.ReplayId));
                }
            }
        }
        finally
        {
            thread.Priority = ThreadPriority.Normal;
            _snapshot = null;
            Status.Current = null;
            Status.Pending = 0;
        }

        BumpRevision();
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

    private static bool JustWritten(string path)
    {
        try
        {
            return DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < Settle;
        }
        catch
        {
            return false;
        }
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
        var row = _db.GetFile(path);
        var build = row?.Build;
        var early = DateTime.UtcNow - mtime < Settle;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            // A replay written to a moment ago is counted only from bytes that were checked to be a complete replay.
            // One that has settled is counted as it is, as it always was.
            var bytes = TakeSnapshot(path, size, mtime.Ticks);
            if (bytes == null && early)
            {
                bytes = ReadIfCountable(path, size, mtime, out _);
                if (bytes == null)
                {
                    // The replay id stays on the row: it is what says, next time, that this file is still to be counted.
                    _db.UpsertFile(path, size, mtime.Ticks, row?.ReplayId, "live", null, build);
                    return null;
                }
            }
            bytes ??= ReplayFiles.ReadAll(path);
            if (!early && ReplayFiles.Inspect(bytes) is { Complete: false, State: ReplayHeaderState.Finished } odd)
            {
                Log.Info($"{fi.Name}: {odd.Problem}; counted as it is");
            }
            var trace = ReplayFiles.Parse(bytes);
            var after = new FileInfo(path);
            if (!after.Exists || after.Length != size || after.LastWriteTimeUtc != mtime)
            {
                // Fortnite (or a copy in progress) wrote to the file while it was being read: what was read is not
                // what is there now. Nothing is stored; the next scan looks at the file again.
                Log.Info($"{fi.Name}: changed while it was being read, will be read again");
                return null;
            }
            _deferred.Remove(path);
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
            var took = early ? $" ({sw.ElapsedMilliseconds} ms, {(DateTime.UtcNow - mtime).TotalMilliseconds:F0} ms after Fortnite wrote it)" : "";
            Log.Info($"{fi.Name}: {(summary.Counted ? $"mine={summary.Count.Mine} squad={summary.Count.MineAndSquad}" : "skipped: " + summary.SkipReason)}{took}");
            return summary;
        }
        catch (Exception ex)
        {
            if (early)
            {
                // Read right after the match and it failed: perhaps Fortnite was not quite done with the file.
                // Not an error yet; the file is read again once it has settled.
                Log.Info($"{fi.Name}: early read failed ({ex.GetType().Name}: {ex.Message}), will be read again once the file has settled");
                _deferred[path] = (size, mtime.Ticks, mtime + Settle, null);
                _db.UpsertFile(path, size, mtime.Ticks, row?.ReplayId, "live", null, build);
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
        Volatile.Read(ref _revisionChanged).TrySetResult(); // the window's open request returns, the web server can stop
        _watcher?.Dispose();
        _thread?.Join(TimeSpan.FromSeconds(5));
    }
}
