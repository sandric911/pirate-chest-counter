using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using PirateChest.Core.Counting;
using PirateChest.Core.Trace;

namespace PirateChest.Core.Tests;

/// <summary>
/// Anonymized traces exported from real v43 replays. Expected values are a snapshot; set
/// REGRESSION_UPDATE=1 to rewrite expected.json after an intentional rules change.
/// </summary>
public class RegressionTests
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() }, WriteIndented = true };
    private static string Dir => Path.Combine(AppContext.BaseDirectory, "Regression");

    public sealed record Expected(int Mine, int Squad, int MineBroken, int MineHeuristic, int BrokenAfterOpen, int Normal, int Rare, int Sprite);

    public static IEnumerable<object[]> Matches() =>
        Directory.GetFiles(Dir, "match*.json.gz").Select(f => new object[] { Path.GetFileName(f) }).OrderBy(x => (string)x[0]);

    private static MatchTrace Load(string file)
    {
        using var gz = new GZipStream(File.OpenRead(Path.Combine(Dir, file)), CompressionMode.Decompress);
        return JsonSerializer.Deserialize<MatchTrace>(gz, Json)!;
    }

    private static Expected Measure(MatchTrace t)
    {
        var s = new MatchAnalyzer().Analyze(t);
        Assert.True(s.Counted, s.SkipReason);
        var c = s.Count;
        var mine = c.Credits.Where(x => x.By == Who.Me).ToList();
        return new Expected(c.Mine, c.MineAndSquad, c.MineBroken, c.MineHeuristic, c.BrokenAfterOpen,
            mine.Count(x => x.Type == ChestType.Normal), mine.Count(x => x.Type == ChestType.Rare), mine.Count(x => x.Type == ChestType.Sprite));
    }

    [Theory]
    [MemberData(nameof(Matches))]
    public void RealMatch_CountsMatchSnapshot(string file)
    {
        var actual = Measure(Load(file));
        var expectedFile = Path.Combine(Dir, "expected.json");
        var all = File.Exists(expectedFile) ? JsonSerializer.Deserialize<Dictionary<string, Expected>>(File.ReadAllText(expectedFile), Json)! : new();

        if (Environment.GetEnvironmentVariable("REGRESSION_UPDATE") == "1")
        {
            lock (Json)
            {
                var src = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Regression", "expected.json"));
                var current = File.Exists(src) ? JsonSerializer.Deserialize<Dictionary<string, Expected>>(File.ReadAllText(src), Json)! : new();
                current[file] = actual;
                File.WriteAllText(src, JsonSerializer.Serialize(current.OrderBy(kv => kv.Key).ToDictionary(), Json));
            }
            return;
        }

        Assert.True(all.ContainsKey(file), $"no snapshot for {file}; run with REGRESSION_UPDATE=1");
        Assert.Equal(all[file], actual);
    }

    [Theory]
    [MemberData(nameof(Matches))]
    public void RealMatch_Invariants(string file)
    {
        var t = Load(file);
        var c = new MatchCounter(options: new CountingOptions { CountReopenedAfterReset = false }).Count(t);
        // With strict counting, never more than one credit per chest.
        Assert.Equal(c.Credits.Count, c.Credits.Select(x => x.ChestKey).Distinct().Count());
        // Squad counter is never below mine.
        Assert.True(c.MineAndSquad >= c.Mine);
        // Only chests are credited.
        Assert.All(c.Credits, x => Assert.Equal(ContainerRole.Chest, ChestClassifier.Default.RoleOf(x.ClassName, out _)));
    }
}
