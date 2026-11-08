namespace PirateChest.Core.Trace;

/// <summary>
/// Raw, parser-independent description of one match, extracted from a replay.
/// Everything the counting rules need, nothing more. Serializable to JSON so that
/// regression tests can run on anonymized traces instead of .replay files.
/// </summary>
public sealed class MatchTrace
{
    public ReplayMeta Meta { get; set; } = new();

    /// <summary>Players (PlayerState actors) seen in the replay.</summary>
    public List<TracePlayer> Players { get; set; } = new();

    /// <summary>Pawn actor id → PlayerState actor id.</summary>
    public Dictionary<uint, uint> PawnToPlayerState { get; set; } = new();

    /// <summary>Position samples for pawns of the replay owner and teammates (others are dropped).</summary>
    public List<PawnSample> PawnSamples { get; set; } = new();

    /// <summary>Container (chest, ammo box, ...) events in chronological order.</summary>
    public List<ContainerEvent> ContainerEvents { get; set; } = new();

    /// <summary>Damage / gameplay-cue hints that name an instigator for a container.</summary>
    public List<DamageHint> DamageHints { get; set; } = new();
}

public sealed class ReplayMeta
{
    /// <summary>Stable replay id from the header (Guid). Used to skip already processed files.</summary>
    public string ReplayId { get; set; } = "";
    public string? Branch { get; set; }
    public uint Changelist { get; set; }
    public int BuildMajor { get; set; }
    public int BuildMinor { get; set; }
    public DateTime TimestampUtc { get; set; }
    public uint LengthMs { get; set; }
    public bool IsLive { get; set; }
    public string? Playlist { get; set; }
    public string? MapInfo { get; set; }
    public int? TeamSize { get; set; }
    public float? MatchEndTime { get; set; }
    /// <summary>Owner's death time in replay seconds, if the owner died.</summary>
    public float? OwnerDeathTime { get; set; }
    public int? OwnerPlacement { get; set; }
    public string ParserVersion { get; set; } = "";
}

public sealed class TracePlayer
{
    /// <summary>PlayerState actor id (NetGUID value) within this replay.</summary>
    public uint StateId { get; set; }
    public int? TeamIndex { get; set; }
    public bool IsReplayOwner { get; set; }
    public bool IsBot { get; set; }
}

public sealed class PawnSample
{
    public uint PawnId { get; set; }
    public float Time { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    /// <summary>True while skydiving / gliding.</summary>
    public bool InAir { get; set; }
}

public enum ContainerEventKind
{
    /// <summary>Actor channel opened: the container entered the replay's relevancy.</summary>
    Appeared = 0,
    /// <summary>Replicated properties changed.</summary>
    Update = 1,
    /// <summary>Actor channel closed. See <see cref="ContainerEvent.CloseReason"/>.</summary>
    Closed = 2,
}

public enum CloseReason
{
    Destroyed = 0,
    Dormancy = 1,
    LevelUnloaded = 2,
    Relevancy = 3,
    TearOff = 4,
    Unknown = 99,
}

public sealed class ContainerEvent
{
    public float Time { get; set; }
    public ContainerEventKind Kind { get; set; }
    /// <summary>NetGUID of the actor (static actors keep it across relevancy).</summary>
    public uint ActorId { get; set; }
    /// <summary>Static actor name inside the level (e.g. Tiered_Chest_Athena_C_123), if static.</summary>
    public string? StaticName { get; set; }
    /// <summary>Short class name, e.g. Tiered_Chest_Athena_C.</summary>
    public string ClassName { get; set; } = "";
    /// <summary>True if this update is the initial state right after the channel opened.</summary>
    public bool IsInitial { get; set; }

    public bool? AlreadySearched { get; set; }
    public uint? SearchingPawn { get; set; }
    /// <summary>SearchAnimationCount: increments on every search (also on "break unopened", which spills loot).</summary>
    public uint? SearchCount { get; set; }
    public bool? InstantDeath { get; set; }
    /// <summary>Treasure Hunt animated chest: the regular container it popped out of.</summary>
    public uint? SourceContainer { get; set; }
    public int? LootTier { get; set; }
    public bool? Destroyed { get; set; }
    public ushort? Health { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Z { get; set; }

    public CloseReason? CloseReason { get; set; }

    /// <summary>Position of <see cref="SearchingPawn"/> at the moment of this update (static chests have no own location).</summary>
    public double? SearcherX { get; set; }
    public double? SearcherY { get; set; }
    public double? SearcherZ { get; set; }

    /// <summary>Raw changed fields (diagnostics only, not used by the rules).</summary>
    public Dictionary<string, string>? Raw { get; set; }
}

public sealed class DamageHint
{
    public float Time { get; set; }
    public uint ActorId { get; set; }
    /// <summary>Instigator actor id (pawn or controller) if known.</summary>
    public uint InstigatorId { get; set; }
    public string Source { get; set; } = "";
}
