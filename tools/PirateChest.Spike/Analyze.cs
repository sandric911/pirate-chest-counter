using System.Text.Json;
using System.Text.Json.Serialization;
using PirateChest.Core.Counting;
using PirateChest.Core.Trace;

namespace PirateChest.Spike;

public static class Analyze
{
    public static void Run(string tracesDir)
    {
        var json = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var atlas = new ChestAtlas();
        var all = Directory.GetFiles(tracesDir, "*.json").OrderBy(x => x).ToList();
        foreach (var f in all) atlas.Learn(JsonSerializer.Deserialize<MatchTrace>(File.ReadAllText(f), json)!);
        Console.WriteLine($"atlas: {atlas.Entries.Count} static chests, median samples {atlas.Entries.Values.Select(e => e.N).OrderBy(n => n).ElementAtOrDefault(atlas.Entries.Count / 2)}");
        var analyzer = new MatchAnalyzer(locator: atlas);
        int mine = 0, squad = 0, broken = 0, heur = 0, mheur = 0, unattr = 0, foreign = 0, bao = 0, counted = 0;
        var types = new Dictionary<ChestType, int>();
        foreach (var f in Directory.GetFiles(tracesDir, "*.json").OrderBy(x => x))
        {
            var trace = JsonSerializer.Deserialize<MatchTrace>(File.ReadAllText(f), json)!;
            var s = analyzer.Analyze(trace);
            var name = Path.GetFileName(f).Replace("UnsavedReplay-", "").Replace(".replay.json", "");
            if (!s.Counted)
            {
                Console.WriteLine($"{name}  SKIP: {s.SkipReason}");
                continue;
            }
            counted++;
            var c = s.Count;
            mine += c.Mine; squad += c.MineAndSquad; broken += c.MineBroken; heur += c.SquadHeuristic; mheur += c.MineHeuristic; unattr += c.UnattributedDestroyed; foreign += c.ForeignChests; bao += c.BrokenAfterOpen;
            foreach (var cr in c.Credits.Where(x => x.By == Who.Me)) types[cr.Type] = types.GetValueOrDefault(cr.Type) + 1;
            Console.WriteLine($"{name}  {MatchFilter.ModeLabel(s.Meta.Playlist),-22} mine={c.Mine,3} squad={c.MineAndSquad,3} brk={c.MineBroken,2} heur={c.SquadHeuristic} unattr={c.UnattributedDestroyed} land={s.LandingTime:F0}s end={s.ActiveEndTime:F0}s cpm={s.ChestsPerMinute:F2} unk=[{string.Join(",", c.UnknownChestClasses)}]");
        }
        Console.WriteLine($"MATCHES={counted} MINE={mine} SQUAD={squad} MINE_BROKEN={broken} HEUR={heur} MINE_HEUR={mheur} UNATTRIBUTED={unattr} FOREIGN={foreign} BROKEN_AFTER_OPEN={bao}");
        Console.WriteLine("types: " + string.Join(", ", types.Select(kv => $"{kv.Key}={kv.Value}")));
    }
}
