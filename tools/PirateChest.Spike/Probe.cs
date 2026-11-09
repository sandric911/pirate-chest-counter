using PirateChest.Core.Counting;
using PirateChest.Core.Trace;
using PirateChest.ReplayReader;

namespace PirateChest.Spike;

/// <summary>
/// probe &lt;file.replay&gt;: who the replay thinks the recorder is over time, when squad members died, and who searched
/// each chest the counter credits to me or my squad. For checking attribution on one real match.
/// </summary>
public static class Probe
{
    public static void Run(string file)
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var reader = new ChestReplayReader();
        var replay = reader.ReadReplay(fs);
        var trace = reader.BuildTrace(replay);
        static string T(double t) => $"{(int)t / 60}:{(int)t % 60:00}";

        Console.WriteLine($"length {T(trace.Meta.LengthMs / 1000.0)}, GameData.RecorderId={replay.GameData?.RecorderId}, placement={trace.Meta.OwnerPlacement}, owner death={trace.Meta.OwnerDeathTime}");
        Console.WriteLine("RecorderPlayerState changes:");
        foreach (var (time, state) in reader.RecorderHistory)
        {
            Console.WriteLine($"  {T(time)}  state {state}");
        }

        var owner = trace.Players.FirstOrDefault(p => p.IsReplayOwner);
        var recorders = reader.RecorderHistory.Select(r => r.State).ToHashSet();
        var team = owner?.TeamIndex;
        Console.WriteLine($"Squad (team {team}) and every state that was ever the recorder:");
        foreach (var p in trace.Players.Where(p => (team != null && p.TeamIndex == team) || recorders.Contains(p.StateId)).OrderBy(p => p.StateId))
        {
            var died = reader.DeathTimes.TryGetValue(p.StateId, out var d) ? T(d) : "-";
            var pawns = string.Join(",", trace.PawnToPlayerState.Where(kv => kv.Value == p.StateId).Select(kv => kv.Key));
            Console.WriteLine($"  state {p.StateId} team {p.TeamIndex} owner={p.IsReplayOwner} died={died} pawns=[{pawns}]");
        }

        var resolver = new PlayerResolver(trace);
        var result = new MatchAnalyzer().Analyze(trace);
        Console.WriteLine($"Counted: mine={result.Count.Mine} squad={result.Count.MineAndSquad}");
        Console.WriteLine("Credits (time, type, by, kind, searching pawn → state):");
        var pawnAt = trace.ContainerEvents
            .Where(e => e.SearchingPawn is { } sp && sp != 0)
            .GroupBy(e => e.ActorId)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Time).ToList());
        foreach (var c in result.Count.Credits.OrderBy(c => c.Time))
        {
            Console.WriteLine($"  {T(c.Time)}  {c.Type,-6} {c.By,-8} {c.Kind,-14} {c.Method,-9} {c.ClassName}");
        }
        // PROBE_CHEST=<part of a chest's static name>: every event of that chest, to see how a credit came about.
        if (Environment.GetEnvironmentVariable("PROBE_CHEST") is { Length: > 0 } part)
        {
            Console.WriteLine($"All events of chests matching '{part}':");
            foreach (var e in trace.ContainerEvents.Where(e => e.StaticName?.Contains(part) == true).OrderBy(e => e.Time))
            {
                Console.WriteLine($"  {T(e.Time)} ({e.Time:F1}s) actor {e.ActorId} {e.Kind} initial={e.IsInitial} searched={e.AlreadySearched} pawn={e.SearchingPawn} count={e.SearchCount} tier={e.LootTier} destroyed={e.Destroyed} close={e.CloseReason}");
            }
        }
        // PROBE_AT="seconds;x;y;z": where each squad member was (distance to the point) in the 4 s around that moment.
        if (Environment.GetEnvironmentVariable("PROBE_AT") is { Length: > 0 } at)
        {
            var v = at.Split(';').Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            Console.WriteLine($"Squad near ({v[1]:F0},{v[2]:F0},{v[3]:F0}) at {T(v[0])}:");
            foreach (var g in trace.PawnSamples.Where(s => Math.Abs(s.Time - v[0]) <= 4).GroupBy(s => s.PawnId))
            {
                var best = g.OrderBy(s => Math.Abs(s.Time - v[0])).First();
                var d = Math.Sqrt(Math.Pow(best.X - v[1], 2) + Math.Pow(best.Y - v[2], 2) + Math.Pow(best.Z - v[3], 2)) / 100;
                var st = trace.PawnToPlayerState.TryGetValue(g.Key, out var s2) ? s2 : 0;
                Console.WriteLine($"  pawn {g.Key} (state {st}, {resolver.Resolve(g.Key)}) at {best.Time:F1}s: {d:F1} m away, at ({best.X:F0},{best.Y:F0},{best.Z:F0})");
            }
        }
        Console.WriteLine("Searching-pawn changes on chests (time, pawn → state, resolved):");
        foreach (var e in trace.ContainerEvents.Where(e => e.SearchingPawn is { } sp && sp != 0).OrderBy(e => e.Time))
        {
            var st = trace.PawnToPlayerState.TryGetValue(e.SearchingPawn!.Value, out var s) ? s.ToString() : "?";
            Console.WriteLine($"  {T(e.Time)}  {e.ClassName,-28} tier={e.LootTier} pawn {e.SearchingPawn} → state {st} → {resolver.Resolve(e.SearchingPawn)} searched={e.AlreadySearched}");
        }
    }
}
