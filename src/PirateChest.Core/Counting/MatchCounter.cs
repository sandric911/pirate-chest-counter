using PirateChest.Core.Trace;

namespace PirateChest.Core.Counting;

public enum Who
{
    Me,
    Teammate,
    Other,
    /// <summary>Pawn could not be resolved to a PlayerState (far away / never replicated).</summary>
    Unknown,
}

public enum CountKind
{
    /// <summary>Открыт.</summary>
    Opened,
    /// <summary>Сломан неоткрытым (засчитывается как открытый).</summary>
    BrokenUnopened,
}

public enum Attribution
{
    /// <summary>Taken from the replay (SearchingPawn).</summary>
    Replay,
    /// <summary>Distance heuristic (no instigator in the replay).</summary>
    Heuristic,
}

public sealed class CountingOptions
{
    /// <summary>Heuristic radius for "who broke it" when the replay has no instigator, meters.</summary>
    public double HeuristicRadiusMeters { get; set; } = 15;
    /// <summary>Heuristic time window around the destruction, seconds.</summary>
    public double HeuristicWindowSeconds { get; set; } = 2;
    /// <summary>
    /// A search (SearchAnimationCount+1) without bAlreadySearched, followed by the actor's destruction
    /// within this many seconds, is a "broken unopened": the game spills the loot (a search by the
    /// breaker) and destroys the chest; the channel closes ~5 s later.
    /// </summary>
    public double BreakLinkSeconds { get; set; } = 7;
    /// <summary>Dynamic chests (buried, relic) get a new NetGUID each time they re-enter relevancy;
    /// spawns of the same class closer than this (XY, Unreal units) are the same chest.</summary>
    public double DynamicSameChestUnits { get; set; } = 300;
    /// <summary>
    /// In v43 the game periodically resets opened chests (they close and can be searched again). When true, every
    /// real search counts — also a second search of the same chest after such a reset. A chest that merely
    /// re-enters relevancy already open is never counted again either way.
    /// </summary>
    public bool CountReopenedAfterReset { get; set; } = true;
    /// <summary>Unreal units per meter.</summary>
    public const double UnitsPerMeter = 100;
}

/// <summary>One chest credited to me or a teammate.</summary>
public sealed class ChestCredit
{
    public string ChestKey { get; set; } = "";
    public string ClassName { get; set; } = "";
    public ChestType Type { get; set; }
    public int? LootTier { get; set; }
    public CountKind Kind { get; set; }
    public Who By { get; set; }
    public Attribution Method { get; set; }
    public float Time { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Z { get; set; }
    /// <summary>The searching pawn the credit was given by (diagnostics; not stored).</summary>
    public uint? Pawn { get; set; }
}

public sealed class MatchCount
{
    public List<ChestCredit> Credits { get; } = new();

    /// <summary>«Мои»: opened by me + broken unopened by me.</summary>
    public int Mine => Credits.Count(c => c.By == Who.Me);
    /// <summary>«Мои + отряд (не меньше)».</summary>
    public int MineAndSquad => Credits.Count(c => c.By is Who.Me or Who.Teammate);
    public int MineBroken => Credits.Count(c => c.By == Who.Me && c.Kind == CountKind.BrokenUnopened);
    public int SquadBroken => Credits.Count(c => c.By is Who.Me or Who.Teammate && c.Kind == CountKind.BrokenUnopened);
    public int MineHeuristic => Credits.Count(c => c.By == Who.Me && c.Method == Attribution.Heuristic);
    public int SquadHeuristic => Credits.Count(c => c.By is Who.Me or Who.Teammate && c.Method == Attribution.Heuristic);

    // Diagnostics (not counted anywhere).
    /// <summary>Chests searched by players outside the squad (or already open when first seen).</summary>
    public int ForeignChests { get; set; }
    /// <summary>Rule 5: broken after being opened.</summary>
    public int BrokenAfterOpen { get; set; }
    /// <summary>Destroyed unopened, no instigator in the replay and no squad member close enough.</summary>
    public int UnattributedDestroyed { get; set; }
    /// <summary>Squad searched a chest again after the game reset it (rule 2: still counted once).</summary>
    public int ReopenedAfterReset { get; set; }
    public HashSet<string> UnknownChestClasses { get; } = new();
}

/// <summary>Known chest locations (static chests carry no position in the replay).</summary>
public interface IChestLocator
{
    (double X, double Y, double Z)? Locate(string chestKey);
}

/// <summary>
/// The counting rules (section 2 of the spec). Pure function of a <see cref="MatchTrace"/>.
///
/// What the replay shows (verified on v43.00 client replays):
///  * open:            bAlreadySearched 0→1, SearchAnimationCount+1, SearchingPawn = opener;
///  * break unopened:  SearchAnimationCount+1, SearchingPawn = breaker, bDestroyed=1 (+bInstantDeath),
///                     no bAlreadySearched; channel closes (reason Destroyed) ~5 s later;
///  * break opened:    bDestroyed=1 without a new search;
///  * the chest's channel also closes with reason "Destroyed" when the client unloads it (streaming /
///    relevancy) — so only bDestroyed means "broken"; dynamic chests then come back with a new NetGUID.
/// </summary>
public sealed class MatchCounter
{
    private readonly ChestClassifier _classifier;
    private readonly CountingOptions _options;
    private readonly IChestLocator? _locator;

    public MatchCounter(ChestClassifier? classifier = null, CountingOptions? options = null, IChestLocator? locator = null)
    {
        _classifier = classifier ?? ChestClassifier.Default;
        _options = options ?? new CountingOptions();
        _locator = locator;
    }

    private sealed class ChestState
    {
        public string Key = "";
        public string ClassName = "";
        public bool Seen;
        public bool Searched;
        public uint SearchCount;
        public uint? Pawn;
        public int? Tier;
        public bool Credited;
        public bool Foreign;
        public bool Destroyed;
        public double? X, Y, Z;
        public Search? Pending;
    }

    private sealed record Search(float Time, uint? Pawn, double? X, double? Y, double? Z);

    private sealed class Run
    {
        public readonly MatchCount Result = new();
        public required PlayerResolver Resolver;
        public readonly List<(ChestState St, float Time)> Heuristic = new();
    }

    public MatchCount Count(MatchTrace trace)
    {
        var run = new Run { Resolver = new PlayerResolver(trace) };
        var keys = AssignKeys(trace);
        var chests = new Dictionary<string, ChestState>();
        // Unknown chest-like classes are not counted, but reported so a new chest kind is noticed after an update.
        foreach (var e in trace.ContainerEvents)
        {
            if (e.AlreadySearched == true && !_classifier.IsKnown(e.ClassName) && e.ClassName.Contains("Chest", StringComparison.OrdinalIgnoreCase))
            {
                run.Result.UnknownChestClasses.Add(e.ClassName);
            }
        }

        foreach (var e in trace.ContainerEvents)
        {
            if (!keys.TryGetValue(e.ActorId, out var key))
            {
                continue; // not a chest
            }
            if (!chests.TryGetValue(key, out var st))
            {
                st = new ChestState { Key = key, ClassName = e.ClassName };
                chests[key] = st;
            }

            // A pending search that was not followed by destruction in time is a plain open.
            if (st.Pending != null && e.Time - st.Pending.Time > _options.BreakLinkSeconds)
            {
                Resolve(st, CountKind.Opened, st.Pending, run);
            }

            switch (e.Kind)
            {
                case ContainerEventKind.Appeared:
                    if (e.X != null && st.X == null)
                    {
                        (st.X, st.Y, st.Z) = (e.X, e.Y, e.Z);
                    }
                    break;
                case ContainerEventKind.Update:
                    OnUpdate(st, e, run);
                    break;
                case ContainerEventKind.Closed:
                    if (st.Pending != null && e.CloseReason == CloseReason.Destroyed && e.Time - st.Pending.Time <= _options.BreakLinkSeconds)
                    {
                        Resolve(st, CountKind.BrokenUnopened, st.Pending, run);
                        st.Destroyed = true;
                    }
                    break;
            }
        }

        foreach (var st in chests.Values)
        {
            if (st.Pending != null)
            {
                Resolve(st, CountKind.Opened, st.Pending, run);
            }
        }

        ApplyHeuristic(trace, run);
        run.Result.Credits.Sort((a, b) => a.Time.CompareTo(b.Time));
        return run.Result;
    }

    /// <summary>
    /// Rule 2 key: static actor name (stable within the replay), or for dynamic chests the class plus
    /// spawn coordinates, so a dynamic chest that re-enters relevancy with a new NetGUID is the same chest.
    /// </summary>
    private Dictionary<uint, string> AssignKeys(MatchTrace trace)
    {
        var keys = new Dictionary<uint, string>();
        var dynamicSpots = new List<(string Class, double X, double Y, string Key)>();
        var limit2 = _options.DynamicSameChestUnits * _options.DynamicSameChestUnits;

        foreach (var e in trace.ContainerEvents)
        {
            if (keys.ContainsKey(e.ActorId) || _classifier.RoleOf(e.ClassName, out _) != ContainerRole.Chest)
            {
                continue;
            }
            if (e.StaticName != null)
            {
                keys[e.ActorId] = e.StaticName;
                continue;
            }
            if (e.X is { } x && e.Y is { } y)
            {
                var match = dynamicSpots.FirstOrDefault(d => d.Class == e.ClassName && (d.X - x) * (d.X - x) + (d.Y - y) * (d.Y - y) <= limit2);
                if (match.Key != null)
                {
                    keys[e.ActorId] = match.Key;
                }
                else
                {
                    var key = $"dyn:{e.ClassName}:{Math.Round(x)}:{Math.Round(y)}";
                    dynamicSpots.Add((e.ClassName, x, y, key));
                    keys[e.ActorId] = key;
                }
                continue;
            }
            keys[e.ActorId] = $"dyn:{e.ActorId}";
        }
        return keys;
    }

    private void OnUpdate(ChestState st, ContainerEvent e, Run run)
    {
        var firstSight = !st.Seen;
        st.Seen = true;

        if (e.LootTier is { } tier)
        {
            st.Tier = tier;
        }
        if (e.SearchingPawn is { } pawn)
        {
            st.Pawn = pawn == 0 ? null : pawn;
        }
        else if (e.IsInitial)
        {
            // An initial bunch carries the full state, so an absent SearchingPawn means "none known" — not the pawn
            // of an earlier search of this chest. Otherwise a chest I opened, that the game reset and someone else
            // searched while it was out of view (e.g. while I was dead and spectating), came back credited to me.
            st.Pawn = null;
        }
        if (e.X != null && st.X == null)
        {
            (st.X, st.Y, st.Z) = (e.X, e.Y, e.Z);
        }

        var prevSearched = st.Searched;
        var prevCount = st.SearchCount;
        if (e.SearchCount is { } sc)
        {
            st.SearchCount = sc;
        }
        var destroyedNow = e.Destroyed == true || e.InstantDeath == true;
        var here = new Search(e.Time, st.Pawn, e.SearcherX, e.SearcherY, e.SearcherZ);

        if (e.IsInitial)
        {
            // State when the chest (re-)enters relevancy. Fields equal to defaults are not sent.
            var initiallySearched = e.AlreadySearched == true || (destroyedNow && (e.SearchCount ?? 0) > 0);
            if (initiallySearched && !prevSearched)
            {
                // Rule 7 (first sight) / searched while out of relevancy (re-entry).
                st.Searched = e.AlreadySearched == true;
                var who = run.Resolver.Resolve(st.Pawn);
                if (who is Who.Me or Who.Teammate)
                {
                    Credit(st, destroyedNow ? CountKind.BrokenUnopened : CountKind.Opened, who, Attribution.Replay, here, run);
                }
                else
                {
                    MarkForeign(st, run);
                }
            }
            if (destroyedNow)
            {
                st.Destroyed = true;
            }
            // The initial bunch carries the full state: a field that is absent has its default value. So a chest that
            // comes back unopened was reset while out of view, and its search counter started over.
            st.Searched = e.AlreadySearched == true;
            st.SearchCount = e.SearchCount ?? 0;
            _ = firstSight;
            return;
        }

        if (e.AlreadySearched == false && prevSearched)
        {
            // The game reset the chest (it can be searched again). Rule 2 still applies.
            st.Searched = false;
            prevSearched = false;
        }

        var searchedNow = e.AlreadySearched == true && !prevSearched;
        var countUp = e.SearchCount is { } c && c > prevCount;

        if (searchedNow)
        {
            st.Pending = null;
            Resolve(st, CountKind.Opened, here, run);
        }
        else if (countUp && !prevSearched && !st.Destroyed)
        {
            if (destroyedNow)
            {
                Resolve(st, CountKind.BrokenUnopened, here, run);
                st.Destroyed = true;
                return;
            }
            st.Pending = here;
        }

        if (destroyedNow && !st.Destroyed)
        {
            st.Destroyed = true;
            if (st.Pending != null)
            {
                Resolve(st, CountKind.BrokenUnopened, st.Pending, run);
            }
            else if (st.Searched || prevSearched)
            {
                run.Result.BrokenAfterOpen++; // rule 5
            }
            else
            {
                run.Heuristic.Add((st, e.Time)); // rule 6
            }
        }
    }

    private void Resolve(ChestState st, CountKind kind, Search s, Run run)
    {
        st.Pending = null;
        if (kind == CountKind.Opened)
        {
            st.Searched = true;
        }
        var who = run.Resolver.Resolve(s.Pawn);
        if (who is Who.Me or Who.Teammate)
        {
            Credit(st, kind, who, Attribution.Replay, s, run);
        }
        else
        {
            MarkForeign(st, run);
        }
    }

    private static void MarkForeign(ChestState st, Run run)
    {
        if (!st.Foreign && !st.Credited)
        {
            st.Foreign = true;
            run.Result.ForeignChests++;
        }
    }

    private void Credit(ChestState st, CountKind kind, Who who, Attribution method, Search s, Run run)
    {
        if (st.Credited)
        {
            // A new search of a chest that was already credited in this match: only possible after the game reset it.
            run.Result.ReopenedAfterReset++;
            if (!_options.CountReopenedAfterReset)
            {
                return;
            }
        }
        st.Credited = true;
        run.Result.Credits.Add(new ChestCredit
        {
            ChestKey = st.Key,
            ClassName = st.ClassName,
            Type = _classifier.TypeOf(st.ClassName, st.Tier),
            LootTier = st.Tier,
            Kind = kind,
            By = who,
            Method = method,
            Time = s.Time,
            Pawn = s.Pawn,
        });
        var c = run.Result.Credits[^1];
        if (st.X != null)
        {
            (c.X, c.Y, c.Z) = (st.X, st.Y, st.Z);
        }
        else if (_locator?.Locate(st.Key) is { } known)
        {
            (c.X, c.Y, c.Z) = (known.X, known.Y, known.Z);
        }
        else
        {
            (c.X, c.Y, c.Z) = (s.X, s.Y, s.Z);
        }
    }

    /// <summary>Rule 6: destroyed unopened without an instigator → nearest squad member within R during ±window.</summary>
    private void ApplyHeuristic(MatchTrace trace, Run run)
    {
        if (run.Heuristic.Count == 0)
        {
            return;
        }
        var squad = new SquadPositions(trace, run.Resolver);
        var radius = _options.HeuristicRadiusMeters * CountingOptions.UnitsPerMeter;

        foreach (var (st, time) in run.Heuristic)
        {
            if (st.Credited)
            {
                continue;
            }
            (double X, double Y, double Z)? loc = st.X is { } x ? (x, st.Y ?? 0, st.Z ?? 0) : _locator?.Locate(st.Key);
            var who = loc is { } l ? squad.Nearest(l, time - _options.HeuristicWindowSeconds, time + _options.HeuristicWindowSeconds, radius) : null;
            if (who is not { } w)
            {
                run.Result.UnattributedDestroyed++;
                continue;
            }
            st.Credited = true;
            run.Result.Credits.Add(new ChestCredit
            {
                ChestKey = st.Key,
                ClassName = st.ClassName,
                Type = _classifier.TypeOf(st.ClassName, st.Tier),
                LootTier = st.Tier,
                Kind = CountKind.BrokenUnopened,
                By = w,
                Method = Attribution.Heuristic,
                Time = time,
                X = loc!.Value.X,
                Y = loc.Value.Y,
                Z = loc.Value.Z,
            });
        }
    }
}

/// <summary>Resolves pawn ids to me / teammate / other.</summary>
public sealed class PlayerResolver
{
    private readonly Dictionary<uint, uint> _pawnToState;
    private readonly Dictionary<uint, TracePlayer> _players;
    private readonly TracePlayer? _owner;

    public PlayerResolver(MatchTrace trace)
    {
        _pawnToState = trace.PawnToPlayerState;
        _players = trace.Players.GroupBy(p => p.StateId).ToDictionary(g => g.Key, g => g.First());
        _owner = trace.Players.FirstOrDefault(p => p.IsReplayOwner);
    }

    public bool HasOwner => _owner != null;

    public Who Resolve(uint? pawn)
    {
        if (pawn is not { } p || p == 0 || _owner == null)
        {
            return Who.Unknown;
        }
        if (!_pawnToState.TryGetValue(p, out var stateId) || !_players.TryGetValue(stateId, out var player))
        {
            return Who.Unknown;
        }
        if (player.StateId == _owner.StateId)
        {
            return Who.Me;
        }
        return player.TeamIndex != null && player.TeamIndex == _owner.TeamIndex ? Who.Teammate : Who.Other;
    }
}

/// <summary>Position samples of me and teammates, for the distance heuristic.</summary>
internal sealed class SquadPositions
{
    private readonly List<(Who Who, PawnSample S)> _samples = new();

    public SquadPositions(MatchTrace trace, PlayerResolver resolver)
    {
        foreach (var s in trace.PawnSamples)
        {
            var who = resolver.Resolve(s.PawnId);
            if (who is Who.Me or Who.Teammate)
            {
                _samples.Add((who, s));
            }
        }
    }

    /// <summary>Closest squad member within radius during [from, to]; prefers me on ties.</summary>
    public Who? Nearest((double X, double Y, double Z) loc, double from, double to, double radius)
    {
        Who? best = null;
        var bestD = double.MaxValue;
        foreach (var (who, s) in _samples)
        {
            if (s.Time < from || s.Time > to)
            {
                continue;
            }
            var dx = s.X - loc.X;
            var dy = s.Y - loc.Y;
            var dz = s.Z - loc.Z;
            var d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (d > radius)
            {
                continue;
            }
            if (d < bestD - 1 || (Math.Abs(d - bestD) <= 1 && who == Who.Me))
            {
                best = who;
                bestD = d;
            }
        }
        return best;
    }
}
