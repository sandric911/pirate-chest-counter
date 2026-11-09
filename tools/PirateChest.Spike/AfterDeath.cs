using System.Collections.Concurrent;
using PirateChest.Core.Counting;
using PirateChest.ReplayReader;

namespace PirateChest.Spike;

/// <summary>
/// afterdeath &lt;file.replay&gt;...: for every chest credited to me after my first death, was the crediting pawn mine
/// and alive (moving) at that moment? A credit by a pawn with no position samples around it means I was not there
/// (dead / spectating) and the searcher came from stale data.
/// </summary>
public static class AfterDeath
{
    public static void Run(IEnumerable<string> files)
    {
        var lines = new ConcurrentBag<(string File, string Text)>();
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, file =>
        {
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var reader = new ChestReplayReader();
                var trace = reader.BuildTrace(reader.ReadReplay(fs));
                var owner = trace.Players.FirstOrDefault(p => p.IsReplayOwner);
                if (owner == null || trace.Meta.OwnerDeathTime is not { } death)
                {
                    return;
                }
                var ownerPawns = trace.PawnToPlayerState.Where(kv => kv.Value == owner.StateId).Select(kv => kv.Key).ToHashSet();
                var samples = trace.PawnSamples.GroupBy(s => s.PawnId).ToDictionary(g => g.Key, g => g.OrderBy(s => s.Time).ToList());
                var result = new MatchAnalyzer().Analyze(trace);
                var sb = new System.Text.StringBuilder();
                int ok = 0, bad = 0;
                foreach (var c in result.Count.Credits.Where(c => c.By == Who.Me && c.Time > death).OrderBy(c => c.Time))
                {
                    var pawn = c.Pawn ?? 0;
                    var list = samples.GetValueOrDefault(pawn);
                    var near = list?.Where(s => Math.Abs(s.Time - c.Time) <= 3).OrderBy(s => Math.Abs(s.Time - c.Time)).FirstOrDefault();
                    var first = list?.FirstOrDefault()?.Time;
                    var last = list?.LastOrDefault()?.Time;
                    var alive = near != null;
                    if (alive) ok++; else bad++;
                    sb.AppendLine($"    {c.Time,6:F0}s {c.Type,-6} {c.Kind,-14} {c.Method,-9} pawn {pawn} mine={ownerPawns.Contains(pawn)} samples {first:F0}..{last:F0}s {(alive ? "ALIVE" : "NO SAMPLES NEAR → suspicious")}");
                }
                lines.Add((Path.GetFileName(file), $"{Path.GetFileName(file)}: death {death:F0}s, my pawns [{string.Join(",", ownerPawns)}], after-death credits ok={ok} suspicious={bad}\n{sb}"));
            }
            catch (Exception ex)
            {
                lines.Add((Path.GetFileName(file), $"{Path.GetFileName(file)}: {ex.GetType().Name} {ex.Message}"));
            }
        });
        foreach (var l in lines.OrderBy(l => l.File))
        {
            Console.WriteLine(l.Text);
        }
    }
}
