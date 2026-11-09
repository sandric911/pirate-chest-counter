using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using PirateChest.Core.Trace;
using PirateChest.ReplayReader;

// Phase 1 spike: parse every replay, dump traces + a summary report.
if (args.Length > 0 && args[0] == "analyze")
{
    PirateChest.Spike.Analyze.Run(args[1]);
    return;
}
if (args.Length > 0 && args[0] == "recount")
{
    PirateChest.Spike.Recount.Run(args[1]);
    return;
}
if (args.Length > 0 && args[0] == "afterdeath")
{
    PirateChest.Spike.AfterDeath.Run(args.Skip(1));
    return;
}
if (args.Length > 0 && args[0] == "probe")
{
    PirateChest.Spike.Probe.Run(args[1]);
    return;
}
if (args.Length > 0 && args[0] == "export")
{
    PirateChest.Spike.Export.Run(args[1], args[2], args[3]);
    return;
}
var demos = args.Length > 0 ? args[0] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteGame", "Saved", "Demos");
var outDir = args.Length > 1 ? args[1] : Path.Combine(Environment.CurrentDirectory, "spike-out");
var limit = args.Length > 2 ? int.Parse(args[2]) : int.MaxValue;
Directory.CreateDirectory(Path.Combine(outDir, "traces"));

var filter = args.Length > 3 ? args[3].Split(',') : null;
var files = Directory.GetFiles(demos, "*.replay").Where(f => filter == null || filter.Any(x => f.Contains(x))).OrderBy(f => f).Take(limit).ToArray();
Console.WriteLine($"{files.Length} replays in {demos}");

var json = new JsonSerializerOptions { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, Converters = { new JsonStringEnumConverter() } };
var results = new ConcurrentBag<SpikeResult>();
var done = 0;

Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, file =>
{
    var r = new SpikeResult { File = Path.GetFileName(file) };
    var sw = Stopwatch.StartNew();
    try
    {
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var reader = new ChestReplayReader { CaptureRawFields = Environment.GetEnvironmentVariable("SPIKE_RAW") == "1" };
        var replay = reader.ReadReplay(fs);
        var trace = reader.BuildTrace(replay);
        r.Ok = true;
        r.Branch = trace.Meta.Branch;
        r.Changelist = trace.Meta.Changelist;
        r.Playlist = trace.Meta.Playlist;
        r.MapInfo = trace.Meta.MapInfo;
        r.Timestamp = trace.Meta.TimestampUtc;
        r.LengthMin = trace.Meta.LengthMs / 60000.0;
        r.IsLive = trace.Meta.IsLive;
        r.OwnerFound = trace.Players.Any(p => p.IsReplayOwner);
        r.Players = trace.Players.Count;
        r.OwnerSamples = trace.PawnSamples.Count;
        r.ChestGroups = reader.Diagnostics.ChestGroups.ToList();
        r.Incompatible = reader.Diagnostics.Incompatible.ToList();
        r.Classes = reader.Diagnostics.Classes;
        r.Warnings = reader.Diagnostics.Warnings;
        r.Paths = reader.Diagnostics.Paths.ToList();
        r.Grid = string.Join("; ", reader.MapGrid.Select(kv => $"{kv.Key}=({kv.Value.X:F0},{kv.Value.Y:F0})"));
        r.Placement = $"teamstats={replay.TeamStats?.Position}/{replay.TeamStats?.TotalPlayers} final={trace.Meta.OwnerPlacement}";
        r.EventCounts = trace.ContainerEvents
            .GroupBy(e => $"{e.ClassName}|{e.Kind}{(e.CloseReason is { } cr ? ":" + cr : "")}")
            .ToDictionary(g => g.Key, g => g.Count());
        File.WriteAllText(Path.Combine(outDir, "traces", r.File + ".json"), JsonSerializer.Serialize(trace, json));
    }
    catch (Exception ex)
    {
        r.Ok = false;
        r.Error = ex.GetType().Name + ": " + ex.Message;
    }
    r.Seconds = sw.Elapsed.TotalSeconds;
    results.Add(r);
    var n = Interlocked.Increment(ref done);
    Console.WriteLine($"[{n}/{files.Length}] {r.File} ok={r.Ok} {r.Seconds:F1}s {r.Playlist} {r.Placement} {r.Grid} {r.Error}");
});

var all = results.OrderBy(r => r.File).ToList();
File.WriteAllText(Path.Combine(outDir, "summary.json"), JsonSerializer.Serialize(all, new JsonSerializerOptions(json) { WriteIndented = true }));
Console.WriteLine();
Console.WriteLine($"OK: {all.Count(r => r.Ok)}  FAILED: {all.Count(r => !r.Ok)}");
foreach (var g in all.Where(r => !r.Ok).GroupBy(r => r.Error)) Console.WriteLine($"  fail x{g.Count()}: {g.Key}");
Console.WriteLine("Branches:");
foreach (var g in all.Where(r => r.Ok).GroupBy(r => $"{r.Branch} CL{r.Changelist}")) Console.WriteLine($"  {g.Key}: {g.Count()}");
Console.WriteLine("Playlists:");
foreach (var g in all.Where(r => r.Ok).GroupBy(r => r.Playlist ?? "(null)")) Console.WriteLine($"  {g.Key}: {g.Count()}");
Console.WriteLine("Maps:");
foreach (var g in all.Where(r => r.Ok).GroupBy(r => r.MapInfo ?? "(null)")) Console.WriteLine($"  {g.Key}: {g.Count()}");
Console.WriteLine("Chest groups declared in replays:");
foreach (var g in all.Where(r => r.Ok).SelectMany(r => r.ChestGroups!).GroupBy(x => x).OrderBy(g => g.Key)) Console.WriteLine($"  {g.Key}: in {g.Count()} replays");
Console.WriteLine("Container classes actually replicated (channels / replays):");
foreach (var g in all.Where(r => r.Ok).SelectMany(r => r.Classes!).GroupBy(kv => kv.Key).OrderBy(g => g.Key))
{
    Console.WriteLine($"  {g.Key}: channels={g.Sum(x => x.Value.Channels)} replays={g.Count()}");
    var fields = g.SelectMany(x => x.Value.Fields).GroupBy(f => f.Key).Select(f => $"{f.Key}={f.Sum(x => x.Value)}");
    Console.WriteLine($"     fields: {string.Join(", ", fields)}");
    var bad = g.SelectMany(x => x.Value.BadFields).GroupBy(f => f.Key).Select(f => $"{f.Key}={f.Sum(x => x.Value)}").ToList();
    if (bad.Count > 0) Console.WriteLine($"     BAD: {string.Join(", ", bad)}");
}
Console.WriteLine("Incompatible (modeled upstream fields that failed):");
foreach (var g in all.Where(r => r.Ok).SelectMany(r => r.Incompatible!).GroupBy(x => x).OrderByDescending(g => g.Count())) Console.WriteLine($"  {g.Key}: {g.Count()}");
Console.WriteLine("Owner found in: " + all.Count(r => r.OwnerFound) + "/" + all.Count(r => r.Ok));

class SpikeResult
{
    public string File { get; set; } = "";
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public double Seconds { get; set; }
    public string? Branch { get; set; }
    public uint Changelist { get; set; }
    public string? Playlist { get; set; }
    public string? MapInfo { get; set; }
    public DateTime Timestamp { get; set; }
    public double LengthMin { get; set; }
    public bool IsLive { get; set; }
    public bool OwnerFound { get; set; }
    public int Players { get; set; }
    public int OwnerSamples { get; set; }
    public List<string>? ChestGroups { get; set; }
    public List<string>? Incompatible { get; set; }
    public Dictionary<string, ContainerClassStats>? Classes { get; set; }
    public List<string>? Warnings { get; set; }
    public List<string>? Paths { get; set; }
    public string? Placement { get; set; }
    public string? Grid { get; set; }
    public Dictionary<string, int>? EventCounts { get; set; }
}
