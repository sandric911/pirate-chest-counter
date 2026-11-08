using System.Text.Json;
using System.Text.Json.Serialization;
using PirateChest.Core.Counting;
using PirateChest.Core.Trace;

namespace PirateChest.Spike;

/// <summary>Exports an anonymized, compact trace for regression tests (no names, no account/replay ids).</summary>
public static class Export
{
    public static void Run(string traceFile, string outFile, string label)
    {
        var json = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() }, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
        var t = JsonSerializer.Deserialize<MatchTrace>(File.ReadAllText(traceFile), json)!;
        var cls = ChestClassifier.Default;
        t.Meta.ReplayId = label;
        t.ContainerEvents = t.ContainerEvents
            .Where(e => cls.RoleOf(e.ClassName, out _) != ContainerRole.Ignored)
            .Where(e => e.Kind != ContainerEventKind.Update || e.IsInitial || e.AlreadySearched != null || e.SearchingPawn != null
                        || e.SearchCount != null || e.Destroyed != null || e.InstantDeath != null || e.LootTier != null || e.SourceContainer != null || e.X != null)
            .Select(e => { e.Raw = null; e.Health = null; return e; })
            .ToList();
        // Keep only pawns that matter: squad samples are already filtered by the reader.
        var used = t.ContainerEvents.Where(e => e.SearchingPawn is > 0).Select(e => e.SearchingPawn!.Value).ToHashSet();
        used.UnionWith(t.PawnSamples.Select(s => s.PawnId));
        t.PawnToPlayerState = t.PawnToPlayerState.Where(kv => used.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
        var states = t.PawnToPlayerState.Values.ToHashSet();
        t.Players = t.Players.Where(p => p.IsReplayOwner || states.Contains(p.StateId)).ToList();
        File.WriteAllText(outFile, JsonSerializer.Serialize(t, json));
    }
}
