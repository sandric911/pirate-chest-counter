using System.Collections.Concurrent;
using PirateChest.Core.Counting;
using PirateChest.ReplayReader;

namespace PirateChest.Spike;

/// <summary>
/// recount &lt;dir&gt;: counts every replay in the folder with the current rules and prints one tab-separated line per
/// match (replay id, mine, squad, mine normal/rare/sprite), to compare with what the app has stored.
/// </summary>
public static class Recount
{
    public static void Run(string dir)
    {
        var lines = new ConcurrentBag<string>();
        var files = Directory.GetFiles(dir, "*.replay");
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, file =>
        {
            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var reader = new ChestReplayReader();
                var trace = reader.BuildTrace(reader.ReadReplay(fs));
                var s = new MatchAnalyzer().Analyze(trace);
                var mine = s.Count.Credits.Where(c => c.By == Who.Me).ToList();
                int T(ChestType t) => mine.Count(c => c.Type == t);
                lines.Add($"{trace.Meta.ReplayId}\t{s.Counted}\t{s.Count.Mine}\t{s.Count.MineAndSquad}\t{T(ChestType.Normal)}\t{T(ChestType.Rare)}\t{T(ChestType.Sprite)}");
            }
            catch (Exception ex)
            {
                lines.Add($"ERROR\t{Path.GetFileName(file)}\t{ex.GetType().Name}");
            }
        });
        foreach (var l in lines.OrderBy(l => l))
        {
            Console.WriteLine(l);
        }
    }
}
