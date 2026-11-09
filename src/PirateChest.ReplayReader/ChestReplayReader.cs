using System.Reflection;
using FortniteReplayReader.Models.NetFieldExports;
using PirateChest.Core.Trace;
using Unreal.Core;
using Unreal.Core.Attributes;
using Unreal.Core.Contracts;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;
using CoreCloseReason = PirateChest.Core.Trace.CloseReason;

namespace PirateChest.ReplayReader;

/// <summary>
/// Extends Shiqan's FortniteReplayReader without forking it:
///  * reads container (chest) properties ourselves, including SearchingPawn which the
///    upstream model does not declare;
///  * captures the channel close reason (upstream drops it), so "destroyed" can be told
///    apart from "left relevancy" — same logic as xNocken/replay-reader (closeReason === 0);
///  * tracks pawn → PlayerState → team links and owner/teammate positions.
/// </summary>
public class ChestReplayReader : FortniteReplayReader.ReplayReader
{
    public const string ParserVersion = "2";

    private readonly NetBitReader _cmd = new();
    private float _time;

    private readonly Dictionary<uint, uint> _channelActor = new();
    private readonly HashSet<uint> _containerChannels = new();
    private readonly HashSet<uint> _freshChannels = new();
    private readonly Dictionary<uint, string> _channelClass = new();

    private readonly Dictionary<uint, uint> _pawnToState = new();
    private readonly Dictionary<uint, TracePlayer> _players = new();
    private readonly Dictionary<uint, List<PawnSample>> _samples = new();
    private readonly Dictionary<uint, bool> _pawnSkydiving = new();
    private readonly Dictionary<uint, bool> _pawnParachute = new();
    private readonly Dictionary<uint, float> _deathTime = new();
    private readonly Dictionary<uint, int> _placement = new();
    private uint? _recorderState;
    private readonly Dictionary<uint, PawnSample> _lastPawnPos = new();

    public List<ContainerEvent> ContainerEvents { get; } = new();
    public ParseDiagnostics Diagnostics { get; } = new();

    /// <summary>Every change of GameState.RecorderPlayerState: (replay time, player state id).</summary>
    public List<(float Time, uint State)> RecorderHistory { get; } = new();

    /// <summary>First time each player state carried death tags (diagnostics).</summary>
    public IReadOnlyDictionary<uint, float> DeathTimes => _deathTime;

    /// <summary>Knocked-down state changes of pawns: (replay time, pawn id, knocked) (diagnostics).</summary>
    public List<(float Time, uint Pawn, bool Knocked)> KnockHistory { get; } = new();
    private readonly Dictionary<uint, bool> _pawnKnocked = new();

    /// <summary>When true, every changed container field is recorded in <see cref="ContainerEvent.Raw"/> (diagnostics only).</summary>
    public bool CaptureRawFields { get; set; }

    public ChestReplayReader() : base(null!, ParseMode.Full) { }

    public override void ReadReplayHeader(FArchive archive)
    {
        base.ReadReplayHeader(archive);
        _cmd.EngineNetworkVersion = Replay.Header.EngineNetworkVersion;
        _cmd.NetworkVersion = Replay.Header.NetworkVersion;
        _cmd.ReplayHeaderFlags = Replay.Header.Flags;
        _cmd.NetworkReplayVersion = archive.NetworkReplayVersion;
    }

    public override void ReadDemoFrameIntoPlaybackPackets(FArchive archive)
    {
        // Peek the frame time (upstream keeps it in a local variable).
        var pos = archive.Position;
        if (archive.NetworkVersion >= NetworkVersionHistory.HISTORY_MULTIPLE_LEVELS)
        {
            archive.ReadInt32();
        }
        _time = archive.ReadSingle();
        archive.Seek(pos);
        base.ReadDemoFrameIntoPlaybackPackets(archive);
    }

    protected override void OnChannelOpened(uint channelIndex, NetworkGUID? actor)
    {
        base.OnChannelOpened(channelIndex, actor);
        if (actor != null)
        {
            _channelActor[channelIndex] = actor.Value;
            _freshChannels.Add(channelIndex);
            _containerChannels.Remove(channelIndex);
        }
    }

    public override bool ReceivedSequencedBunch(DataBunch bunch)
    {
        var closing = bunch.bClose;
        var ch = bunch.ChIndex;
        var result = base.ReceivedSequencedBunch(bunch);

        if (closing)
        {
            if (_containerChannels.Remove(ch) && _channelActor.TryGetValue(ch, out var actorId))
            {
                ContainerEvents.Add(new ContainerEvent
                {
                    Time = _time,
                    Kind = ContainerEventKind.Closed,
                    ActorId = actorId,
                    StaticName = StaticNameOf(actorId),
                    ClassName = _channelClass.GetValueOrDefault(ch, ""),
                    CloseReason = bunch.CloseReason switch
                    {
                        ChannelCloseReason.Destroyed => CoreCloseReason.Destroyed,
                        ChannelCloseReason.Dormancy => CoreCloseReason.Dormancy,
                        ChannelCloseReason.LevelUnloaded => CoreCloseReason.LevelUnloaded,
                        ChannelCloseReason.Relevancy => CoreCloseReason.Relevancy,
                        ChannelCloseReason.TearOff => CoreCloseReason.TearOff,
                        _ => CoreCloseReason.Unknown,
                    },
                });
            }
            _channelActor.Remove(ch);
            _freshChannels.Remove(ch);
            _channelClass.Remove(ch);
        }

        return result;
    }

    public override bool ReceiveProperties(FBitArchive archive, NetFieldExportGroup group, uint channelIndex, out INetFieldExportGroup? exportGroup, bool enablePropertyChecksum = true, bool netDeltaUpdate = false)
    {
        if (!netDeltaUpdate && group.PathName.EndsWith("FortPoiManager", StringComparison.Ordinal))
        {
            exportGroup = null!;
            return ReadPoiManager(archive, group, enablePropertyChecksum);
        }

        if (!netDeltaUpdate && ContainerClasses.IsContainerGroup(group.PathName))
        {
            exportGroup = null!;
            return ReadContainer(archive, group, channelIndex, enablePropertyChecksum);
        }

        return base.ReceiveProperties(archive, group, channelIndex, out exportGroup, enablePropertyChecksum, netDeltaUpdate);
    }

    /// <summary>Map grid bounds (A–J × 1–10 on the in-game map). UE5 sends FVector2D as two doubles.</summary>
    public Dictionary<string, (double X, double Y)> MapGrid { get; } = new();

    private bool ReadPoiManager(FBitArchive archive, NetFieldExportGroup group, bool enablePropertyChecksum)
    {
        if (enablePropertyChecksum)
        {
            archive.ReadBit();
        }
        while (true)
        {
            var handle = archive.ReadIntPacked();
            if (handle == 0)
            {
                return true;
            }
            handle--;
            if (!group.IsValidIndex(handle))
            {
                return false;
            }
            var export = group.NetFieldExports[handle];
            var numBits = archive.ReadIntPacked();
            if (numBits == 0)
            {
                continue;
            }
            _cmd.FillBitsFrom(archive, (int)numBits);
            if (export?.Name is { } name && name.StartsWith("WorldGrid", StringComparison.Ordinal))
            {
                if (numBits == 128)
                {
                    MapGrid[name] = (_cmd.ReadDouble(), _cmd.ReadDouble());
                }
                else if (numBits == 64)
                {
                    MapGrid[name] = (_cmd.ReadSingle(), _cmd.ReadSingle());
                }
            }
        }
    }

    private bool ReadContainer(FBitArchive archive, NetFieldExportGroup group, uint channelIndex, bool enablePropertyChecksum)
    {
        if (!_channelActor.TryGetValue(channelIndex, out var actorId))
        {
            return false;
        }

        if (enablePropertyChecksum)
        {
            archive.ReadBit();
        }

        var className = ContainerClasses.ShortName(group.PathName);
        var isInitial = _freshChannels.Remove(channelIndex);
        _containerChannels.Add(channelIndex);
        _channelClass[channelIndex] = className;

        var ev = new ContainerEvent
        {
            Time = _time,
            Kind = ContainerEventKind.Update,
            ActorId = actorId,
            StaticName = StaticNameOf(actorId),
            ClassName = className,
            IsInitial = isInitial,
        };

        var stats = Diagnostics.ClassStats(className);
        if (isInitial)
        {
            stats.Channels++;
        }

        while (true)
        {
            var handle = archive.ReadIntPacked();
            if (handle == 0)
            {
                break;
            }
            handle--;

            if (!group.IsValidIndex(handle))
            {
                Diagnostics.Warn($"{className}: handle {handle} out of range");
                return false;
            }

            var export = group.NetFieldExports[handle];
            var numBits = archive.ReadIntPacked();
            if (numBits == 0)
            {
                continue;
            }

            if (export is null)
            {
                archive.SkipBits(numBits);
                continue;
            }

            _cmd.FillBitsFrom(archive, (int)numBits);
            stats.Field(export.Name, (int)numBits);
            if (CaptureRawFields)
            {
                ev.Raw ??= new Dictionary<string, string>();
                ev.Raw[export.Name] = numBits <= 32 ? $"{numBits}b:{_cmd.ReadBitsToInt((int)numBits)}" : $"{numBits}b";
                _cmd.Reset();
            }

            try
            {
                switch (export.Name)
                {
                    case "bAlreadySearched":
                        ev.AlreadySearched = _cmd.ReadBit();
                        break;
                    case "SearchingPawn":
                    case "OpeningPawn":
                    case "InteractingPawn":
                        ev.SearchingPawn = _cmd.ReadIntPacked();
                        break;
                    case "SearchAnimationCount":
                        ev.SearchCount = _cmd.ReadUInt32();
                        break;
                    case "SourceContainer":
                        ev.SourceContainer = _cmd.ReadIntPacked();
                        break;
                    case "bInstantDeath":
                        ev.InstantDeath = _cmd.ReadBit();
                        break;
                    case "ReplicatedLootTier":
                        ev.LootTier = _cmd.ReadInt32();
                        break;
                    case "bDestroyed":
                        ev.Destroyed = _cmd.ReadBit();
                        break;
                    case "Health":
                        ev.Health = (ushort)_cmd.ReadUInt16();
                        break;
                    case "ChestCurrentLoc":
                    case "ChestLocation":
                        if (numBits == 192)
                        {
                            ev.X = _cmd.ReadDouble();
                            ev.Y = _cmd.ReadDouble();
                            ev.Z = _cmd.ReadDouble();
                        }
                        else
                        {
                            continue;
                        }
                        break;
                    case "ReplicatedMovement":
                        var m = _cmd.SerializeRepMovement(locationQuantizationLevel: VectorQuantization.RoundWholeNumber);
                        if (m.Location is not null)
                        {
                            ev.X = m.Location.X;
                            ev.Y = m.Location.Y;
                            ev.Z = m.Location.Z;
                        }
                        break;
                    default:
                        continue; // not interesting, bits already consumed into _cmd
                }

                if (_cmd.IsError || !_cmd.AtEnd())
                {
                    stats.BadField(export.Name, (int)numBits);
                }
            }
            catch (Exception ex)
            {
                stats.BadField(export.Name, (int)numBits);
                Diagnostics.Warn($"{className}.{export.Name}: {ex.GetType().Name}");
            }
        }

        if (ev.SearchingPawn is { } searcher && searcher != 0 && _lastPawnPos.TryGetValue(searcher, out var sp) && Math.Abs(_time - sp.Time) <= 2f)
        {
            ev.SearcherX = sp.X;
            ev.SearcherY = sp.Y;
            ev.SearcherZ = sp.Z;
        }

        if (isInitial)
        {
            // An actor channel open is the moment the container entered relevancy.
            var appeared = new ContainerEvent
            {
                Time = _time,
                Kind = ContainerEventKind.Appeared,
                ActorId = actorId,
                StaticName = ev.StaticName,
                ClassName = className,
            };
            // Dynamic actors carry their spawn location in the channel-open bunch.
            if (Channels[channelIndex]?.Actor?.Location is { } loc && (actorId & 1) == 0 && (loc.X != 0 || loc.Y != 0))
            {
                appeared.X = loc.X;
                appeared.Y = loc.Y;
                appeared.Z = loc.Z;
            }
            ContainerEvents.Add(appeared);
        }

        ContainerEvents.Add(ev);
        return true;
    }

    protected override void OnExportRead(uint channelIndex, INetFieldExportGroup? exportGroup)
    {
        base.OnExportRead(channelIndex, exportGroup);

        switch (exportGroup)
        {
            case GameState gs:
                if (gs.RecorderPlayerState?.Value is { } rec && rec != 0)
                {
                    if (_recorderState != rec)
                    {
                        RecorderHistory.Add((_time, rec));
                    }
                    _recorderState = rec;
                }
                break;

            case FortPlayerState ps when _channelActor.TryGetValue(channelIndex, out var stateId):
                if (ps.bOnlySpectator == true)
                {
                    break;
                }
                if (!_players.TryGetValue(stateId, out var p))
                {
                    p = new TracePlayer { StateId = stateId };
                    _players[stateId] = p;
                }
                if (ps.TeamIndex > 0)
                {
                    p.TeamIndex = ps.TeamIndex;
                }
                if (ps.bIsABot == true)
                {
                    p.IsBot = true;
                }
                if (ps.DeathTags != null && !_deathTime.ContainsKey(stateId))
                {
                    _deathTime[stateId] = _time;
                }
                if (ps.Place is { } place && place > 0)
                {
                    _placement[stateId] = place;
                }
                break;

            case PlayerPawn pawn when _channelActor.TryGetValue(channelIndex, out var pawnId):
                if (pawn.PlayerState is { } st && st != 0)
                {
                    _pawnToState[pawnId] = st;
                }
                if (pawn.bIsDBNO is { } knocked && _pawnKnocked.GetValueOrDefault(pawnId) != knocked)
                {
                    _pawnKnocked[pawnId] = knocked;
                    KnockHistory.Add((_time, pawnId, knocked));
                }

                // The two flags replicate independently: keep each one's last value.
                if (pawn.bIsSkydiving is { } sky)
                {
                    _pawnSkydiving[pawnId] = sky;
                }
                if (pawn.bIsParachuteOpen is { } chute)
                {
                    _pawnParachute[pawnId] = chute;
                }

                if (pawn.ReplicatedMovement is { } mv && mv.Location is not null)
                {
                    if (!_samples.TryGetValue(pawnId, out var list))
                    {
                        list = new List<PawnSample>();
                        _samples[pawnId] = list;
                    }
                    var inAir = _pawnSkydiving.GetValueOrDefault(pawnId) || _pawnParachute.GetValueOrDefault(pawnId);
                    _lastPawnPos[pawnId] = new PawnSample { PawnId = pawnId, Time = _time, X = mv.Location.X, Y = mv.Location.Y, Z = mv.Location.Z, InAir = inAir };
                    // Throttle to ~4 Hz, but always keep air/ground transitions.
                    if (list.Count == 0 || _time - list[^1].Time >= 0.25f || list[^1].InAir != inAir)
                    {
                        list.Add(new PawnSample { PawnId = pawnId, Time = _time, X = mv.Location.X, Y = mv.Location.Y, Z = mv.Location.Z, InAir = inAir });
                    }
                }
                break;
        }
    }

    protected override void Cleanup()
    {
        // Collect diagnostics from the NetGuid cache before upstream clears it.
        try
        {
            foreach (var (path, group) in _netGuidCache.NetFieldExportGroupMap)
            {
                if (path.Contains("Chest", StringComparison.OrdinalIgnoreCase))
                {
                    Diagnostics.ChestGroups.Add(path);
                }

                foreach (var export in group.NetFieldExports)
                {
                    if (export is { Incompatible: true } && KnownModelFields.IsModeled(path, export.Name))
                    {
                        Diagnostics.Incompatible.Add($"{ContainerClasses.ShortName(path)}.{export.Name}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Diagnostics.Warn("diagnostics: " + ex.Message);
        }

        if (CaptureRawFields)
        {
            foreach (var path in _netGuidCache.NetGuidToPathName.Values)
            {
                if (path.Contains("POI", StringComparison.OrdinalIgnoreCase) || path.Contains("/Maps/", StringComparison.OrdinalIgnoreCase))
                {
                    Diagnostics.Paths.Add(path);
                }
            }
        }

        base.Cleanup();
    }

    private string? StaticNameOf(uint actorId)
    {
        if ((actorId & 1) == 1 && _netGuidCache.TryGetPathName(actorId, out var path))
        {
            return path;
        }
        return null;
    }

    /// <summary>Builds the parser-independent trace after <see cref="ReadReplay(string)"/>.</summary>
    public MatchTrace BuildTrace(FortniteReplayReader.Models.FortniteReplay replay)
    {
        var trace = new MatchTrace();
        var meta = trace.Meta;
        meta.ReplayId = replay.Header?.Guid ?? "";
        meta.Branch = replay.Header?.Branch;
        meta.Changelist = replay.Info?.Changelist ?? 0;
        meta.BuildMajor = Major;
        meta.BuildMinor = Minor;
        // UE writes FDateTime::Now() (local time of the recording PC) into the replay info.
        var ts = replay.Info?.Timestamp ?? DateTime.MinValue;
        meta.TimestampUtc = ts == DateTime.MinValue ? ts : DateTime.SpecifyKind(ts, DateTimeKind.Local).ToUniversalTime();
        meta.LengthMs = replay.Info?.LengthInMs ?? 0;
        meta.IsLive = replay.Info?.IsLive ?? false;
        meta.Playlist = replay.GameData?.CurrentPlaylist;
        meta.MapInfo = replay.GameData?.MapInfo;
        meta.TeamSize = replay.GameData?.TeamSize;
        meta.MatchEndTime = replay.GameData?.MatchEndTime;
        meta.ParserVersion = ParserVersion;

        var recorder = _recorderState ?? replay.GameData?.RecorderId;
        foreach (var p in _players.Values)
        {
            p.IsReplayOwner = recorder.HasValue && p.StateId == recorder.Value;
            trace.Players.Add(p);
        }

        var owner = trace.Players.FirstOrDefault(p => p.IsReplayOwner);
        if (owner != null)
        {
            if (_deathTime.TryGetValue(owner.StateId, out var dt))
            {
                meta.OwnerDeathTime = dt;
            }
            // The replay's own team stats event is authoritative; PlayerState.Place is a fallback.
            if (replay.TeamStats?.Position is { } pos && pos > 0)
            {
                meta.OwnerPlacement = (int)pos;
            }
            else if (_placement.TryGetValue(owner.StateId, out var pl))
            {
                meta.OwnerPlacement = pl;
            }
        }

        trace.PawnToPlayerState = new Dictionary<uint, uint>(_pawnToState);

        var squadStates = trace.Players
            .Where(p => owner != null && (p.IsReplayOwner || (p.TeamIndex != null && p.TeamIndex == owner.TeamIndex)))
            .Select(p => p.StateId)
            .ToHashSet();

        foreach (var (pawnId, list) in _samples)
        {
            if (_pawnToState.TryGetValue(pawnId, out var st) && squadStates.Contains(st))
            {
                trace.PawnSamples.AddRange(list);
            }
        }
        trace.PawnSamples.Sort((a, b) => a.Time.CompareTo(b.Time));

        trace.ContainerEvents = ContainerEvents;
        return trace;
    }
}

public static class ContainerClasses
{
    public static bool IsContainerGroup(string path) =>
        !path.Contains("ClassNetCache", StringComparison.Ordinal) &&
        (path.Contains("/Containers/", StringComparison.OrdinalIgnoreCase) ||
         path.Contains("Chest", StringComparison.OrdinalIgnoreCase));

    public static string ShortName(string path)
    {
        var dot = path.LastIndexOf('.');
        var name = dot >= 0 ? path[(dot + 1)..] : path;
        var slash = name.LastIndexOf('/');
        return slash >= 0 ? name[(slash + 1)..] : name;
    }
}

/// <summary>Field names declared in upstream models, to tell real incompatibilities from "not modeled".</summary>
internal static class KnownModelFields
{
    private static readonly Lazy<Dictionary<string, HashSet<string>>> Map = new(Build);

    public static bool IsModeled(string groupPath, string field) =>
        Map.Value.TryGetValue(groupPath, out var set) && set.Contains(field);

    private static Dictionary<string, HashSet<string>> Build()
    {
        var result = new Dictionary<string, HashSet<string>>();
        var asm = typeof(FortPlayerState).Assembly;
        foreach (var type in asm.GetTypes())
        {
            var g = type.GetCustomAttribute<NetFieldExportGroupAttribute>();
            if (g == null)
            {
                continue;
            }
            var set = new HashSet<string>();
            foreach (var prop in type.GetProperties())
            {
                var a = prop.GetCustomAttribute<NetFieldExportAttribute>();
                if (a != null && a.Type != RepLayoutCmdType.Ignore)
                {
                    set.Add(a.Name);
                }
            }
            result[g.Path] = set;
        }
        return result;
    }
}

public sealed class ParseDiagnostics
{
    public SortedSet<string> ChestGroups { get; } = new();
    public SortedSet<string> Incompatible { get; } = new();
    public SortedSet<string> Paths { get; } = new();
    public Dictionary<string, ContainerClassStats> Classes { get; } = new();
    public List<string> Warnings { get; } = new();

    public ContainerClassStats ClassStats(string cls)
    {
        if (!Classes.TryGetValue(cls, out var s))
        {
            s = new ContainerClassStats();
            Classes[cls] = s;
        }
        return s;
    }

    public void Warn(string msg)
    {
        if (Warnings.Count < 50)
        {
            Warnings.Add(msg);
        }
    }
}

public sealed class ContainerClassStats
{
    public int Channels { get; set; }
    public Dictionary<string, int> Fields { get; } = new();
    public Dictionary<string, int> BadFields { get; } = new();
    public void Field(string name, int bits) => Fields[name] = Fields.GetValueOrDefault(name) + 1;
    public void BadField(string name, int bits) => BadFields[$"{name}({bits}b)"] = BadFields.GetValueOrDefault($"{name}({bits}b)") + 1;
}
