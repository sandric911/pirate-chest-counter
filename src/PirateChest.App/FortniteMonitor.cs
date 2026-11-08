using System.Diagnostics;

namespace PirateChest.App;

/// <summary>
/// Only checks whether the Fortnite process is in the process list (no memory reading, no injection,
/// no overlay). Used for one thing: warning that replay recording seems to be off.
/// </summary>
public sealed class FortniteMonitor : IDisposable
{
    private static readonly string[] ProcessNames = { "FortniteClient-Win64-Shipping" };
    private readonly Database _db;
    private readonly System.Threading.Timer _timer;
    private long? _runId;

    public bool Running { get; private set; }

    public FortniteMonitor(Database db)
    {
        _db = db;
        _timer = new System.Threading.Timer(_ => Poll(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));
    }

    private void Poll()
    {
        try
        {
            var running = false;
            foreach (var name in ProcessNames)
            {
                var ps = Process.GetProcessesByName(name);
                running |= ps.Length > 0;
                foreach (var p in ps)
                {
                    p.Dispose();
                }
            }
            if (running && !Running)
            {
                _runId = _db.FortniteStarted(DateTime.UtcNow);
            }
            else if (!running && Running && _runId is { } id)
            {
                _db.FortniteStopped(id, DateTime.UtcNow);
                _runId = null;
            }
            Running = running;
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
            return "Папка повторов не найдена. Похоже, запись повторов в Fortnite ни разу не включалась.";
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
            return any ? null : $"Похоже, запись повторов выключена: Fortnite был запущен {run.Start.ToLocalTime():dd.MM HH:mm}–{end.ToLocalTime():HH:mm}, а новых реплеев нет.";
        }
        catch
        {
            return null;
        }
    }

    public void Dispose() => _timer.Dispose();
}
