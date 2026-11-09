using System.Diagnostics;

namespace PirateChest.App;

/// <summary>
/// Only checks whether the Fortnite process is in the process list (no memory reading, no injection).
/// Used to warn that replay recording seems to be off, for the session timer and to show the overlay only in game.
/// </summary>
public sealed class FortniteMonitor : IDisposable
{
    private static readonly string[] ProcessNames = { "FortniteClient-Win64-Shipping" };
    private readonly Database _db;
    private readonly System.Threading.Timer _timer;
    private long? _runId;

    public bool Running { get; private set; }

    /// <summary>Ids of the running game processes, from the process list (the game process is never opened).</summary>
    public IReadOnlySet<int> ProcessIds { get; private set; } = new HashSet<int>();

    /// <summary>Raised on a thread-pool thread when Fortnite starts or exits.</summary>
    public event Action<bool>? RunningChanged;

    public FortniteMonitor(Database db)
    {
        _db = db;
        _timer = new System.Threading.Timer(_ => Poll(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5));
    }

    private void Poll()
    {
        try
        {
            var ids = new HashSet<int>();
            foreach (var name in ProcessNames)
            {
                foreach (var p in Process.GetProcessesByName(name))
                {
                    ids.Add(p.Id);
                    p.Dispose();
                }
            }
            ProcessIds = ids;
            var running = ids.Count > 0;
            if (running && !Running)
            {
                _runId = _db.FortniteStarted(DateTime.UtcNow);
            }
            else if (!running && Running && _runId is { } id)
            {
                _db.FortniteStopped(id, DateTime.UtcNow);
                _runId = null;
            }
            var changed = running != Running;
            Running = running;
            if (changed)
            {
                RunningChanged?.Invoke(running);
            }
        }
        catch (Exception ex)
        {
            Log.Error("fortnite monitor", ex);
        }
    }

    /// <summary>
    /// «Похоже, запись повторов выключена»: Fortnite ran long enough for a match, has been closed,
    /// and no replay file appeared during that run.
    /// </summary>
    public string? RecordingWarning(ProcessorStatus ps)
    {
        if (!ps.DemosFound)
        {
            return Lang.T("Папка повторов не найдена. Похоже, запись повторов в Fortnite ни разу не включалась.",
                "The replay folder was not found. It looks like replay recording has never been turned on in Fortnite.");
        }
        var last = _db.LastFortniteRun();
        if (last is not { End: { } end } run || end - run.Start < TimeSpan.FromMinutes(8) || DateTime.UtcNow - end < TimeSpan.FromMinutes(2))
        {
            return null;
        }
        try
        {
            var any = Directory.EnumerateFiles(AppPaths.DemosDir, "*.replay")
                .Any(f => File.GetLastWriteTimeUtc(f) >= run.Start && File.GetLastWriteTimeUtc(f) <= end.AddMinutes(3));
            return any ? null : Lang.T(
                $"Похоже, запись повторов выключена: Fortnite был запущен {run.Start.ToLocalTime():dd.MM HH:mm}–{end.ToLocalTime():HH:mm}, а новых реплеев нет.",
                $"Replay recording seems to be off: Fortnite ran {run.Start.ToLocalTime():dd.MM HH:mm}–{end.ToLocalTime():HH:mm}, but no new replays appeared.");
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _timer.Dispose();
}
