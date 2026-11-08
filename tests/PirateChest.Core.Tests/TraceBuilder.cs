using PirateChest.Core.Trace;

namespace PirateChest.Core.Tests;

/// <summary>Builds synthetic match traces: me (team 1), a teammate (team 1) and a stranger (team 2).</summary>
public sealed class TraceBuilder
{
    public const uint MyState = 100, MateState = 101, OtherState = 102;
    public const uint MyPawn = 200, MatePawn = 201, OtherPawn = 202, GhostPawn = 299;

    private readonly MatchTrace _t = new();
    private uint _nextDynamic = 1000;

    public TraceBuilder()
    {
        _t.Meta = new ReplayMeta
        {
            ReplayId = "test",
            Branch = "++Fortnite+Release-43.00",
            BuildMajor = 43,
            TimestampUtc = new DateTime(2026, 11, 5, 18, 0, 0, DateTimeKind.Utc),
            Playlist = "Playlist_NoBuildBR_Squad",
            LengthMs = 20 * 60 * 1000,
        };
        _t.Players.Add(new TracePlayer { StateId = MyState, TeamIndex = 3, IsReplayOwner = true });
        _t.Players.Add(new TracePlayer { StateId = MateState, TeamIndex = 3 });
        _t.Players.Add(new TracePlayer { StateId = OtherState, TeamIndex = 7 });
        _t.PawnToPlayerState[MyPawn] = MyState;
        _t.PawnToPlayerState[MatePawn] = MateState;
        _t.PawnToPlayerState[OtherPawn] = OtherState;
    }

    public MatchTrace Build() => _t;

    public TraceBuilder Meta(Action<ReplayMeta> edit)
    {
        edit(_t.Meta);
        return this;
    }

    /// <summary>A static chest enters relevancy (channel open + initial state).</summary>
    public TraceBuilder Appear(float t, string name, string cls = "Tiered_Chest_Athena_C", bool searched = false, uint? pawn = null, int tier = 0)
    {
        var id = IdOf(name);
        _t.ContainerEvents.Add(new ContainerEvent { Time = t, Kind = ContainerEventKind.Appeared, ActorId = id, StaticName = name, ClassName = cls });
        _t.ContainerEvents.Add(new ContainerEvent
        {
            Time = t, Kind = ContainerEventKind.Update, ActorId = id, StaticName = name, ClassName = cls, IsInitial = true,
            LootTier = tier,
            AlreadySearched = searched ? true : null,
            SearchCount = searched ? 1u : null,
            SearchingPawn = pawn,
        });
        return this;
    }

    /// <summary>A dynamic chest (buried/relic) spawns at a location; returns its new actor id.</summary>
    public uint AppearDynamic(float t, string cls, double x, double y, bool searched = false, uint? pawn = null)
    {
        var id = _nextDynamic += 2; // even = dynamic
        _t.ContainerEvents.Add(new ContainerEvent { Time = t, Kind = ContainerEventKind.Appeared, ActorId = id, ClassName = cls, X = x, Y = y, Z = 0 });
        _t.ContainerEvents.Add(new ContainerEvent
        {
            Time = t, Kind = ContainerEventKind.Update, ActorId = id, ClassName = cls, IsInitial = true,
            AlreadySearched = searched ? true : null, SearchCount = searched ? 1u : null, SearchingPawn = pawn,
        });
        return id;
    }

    public TraceBuilder Open(float t, string name, uint pawn, string cls = "Tiered_Chest_Athena_C", uint count = 1) =>
        Update(t, name, cls, e => { e.AlreadySearched = true; e.SearchCount = count; e.SearchingPawn = pawn; });

    public TraceBuilder OpenDynamic(float t, uint id, string cls, uint pawn)
    {
        _t.ContainerEvents.Add(new ContainerEvent { Time = t, Kind = ContainerEventKind.Update, ActorId = id, ClassName = cls, AlreadySearched = true, SearchCount = 1, SearchingPawn = pawn });
        return this;
    }

    /// <summary>The game's "break unopened": search by the breaker + destroyed in one update.</summary>
    public TraceBuilder BreakUnopened(float t, string name, uint pawn, string cls = "Tiered_Chest_Athena_C") =>
        Update(t, name, cls, e => { e.SearchCount = 1; e.SearchingPawn = pawn; e.Destroyed = true; e.InstantDeath = true; });

    /// <summary>Destroyed without any search (already opened, or no instigator).</summary>
    public TraceBuilder Destroy(float t, string name, string cls = "Tiered_Chest_Athena_C") =>
        Update(t, name, cls, e => { e.Destroyed = true; e.InstantDeath = true; });

    public TraceBuilder Reset(float t, string name, string cls = "Tiered_Chest_Athena_C") =>
        Update(t, name, cls, e => { e.AlreadySearched = false; e.SearchCount = 0; });

    public TraceBuilder Update(float t, string name, string cls, Action<ContainerEvent> edit)
    {
        var e = new ContainerEvent { Time = t, Kind = ContainerEventKind.Update, ActorId = IdOf(name), StaticName = name, ClassName = cls };
        edit(e);
        _t.ContainerEvents.Add(e);
        return this;
    }

    public TraceBuilder Close(float t, string name, CloseReason reason, string cls = "Tiered_Chest_Athena_C")
    {
        _t.ContainerEvents.Add(new ContainerEvent { Time = t, Kind = ContainerEventKind.Closed, ActorId = IdOf(name), StaticName = name, ClassName = cls, CloseReason = reason });
        return this;
    }

    public TraceBuilder CloseDynamic(float t, uint id, string cls, CloseReason reason)
    {
        _t.ContainerEvents.Add(new ContainerEvent { Time = t, Kind = ContainerEventKind.Closed, ActorId = id, ClassName = cls, CloseReason = reason });
        return this;
    }

    public TraceBuilder TreasureMarker(float t, string sourceName, uint pawn)
    {
        var id = _nextDynamic += 2;
        _t.ContainerEvents.Add(new ContainerEvent { Time = t, Kind = ContainerEventKind.Appeared, ActorId = id, ClassName = "BGA_DelilahTreasureHuntAnimatedChest_C" });
        _t.ContainerEvents.Add(new ContainerEvent { Time = t, Kind = ContainerEventKind.Update, ActorId = id, ClassName = "BGA_DelilahTreasureHuntAnimatedChest_C", IsInitial = true, SourceContainer = IdOf(sourceName), SearchingPawn = pawn });
        return this;
    }

    public TraceBuilder Pawn(uint pawn, float t, double xMeters, double yMeters, bool inAir = false)
    {
        _t.PawnSamples.Add(new PawnSample { PawnId = pawn, Time = t, X = xMeters * 100, Y = yMeters * 100, Z = 0, InAir = inAir });
        return this;
    }

    /// <summary>Stable odd (= static) actor id per name.</summary>
    public static uint IdOf(string name) => ((uint)name.GetHashCode() & 0x7FFFFFFE) | 1u;
}
