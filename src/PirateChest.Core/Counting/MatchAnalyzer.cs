using PirateChest.Core.Trace;

namespace PirateChest.Core.Counting;

/// <summary>Everything the app stores about one match.</summary>
public sealed class MatchSummary
{
    public ReplayMeta Meta { get; set; } = new();
    public string? SkipReason { get; set; }
    public bool Counted => SkipReason == null;
    public MatchCount Count { get; set; } = new();

    public float? LandingTime { get; set; }
    public double? LandingX { get; set; }
    public double? LandingY { get; set; }
    /// <summary>End of active play: last moment the owner's pawn existed (death without reboot or match end).</summary>
    public float? ActiveEndTime { get; set; }
    public float? FirstDeathTime { get; set; }
    public int MineBeforeFirstDeath { get; set; }

    public double ActiveMinutes =>
        LandingTime is { } l && ActiveEndTime is { } e && e > l ? (e - l) / 60.0 : 0;

    public double? ChestsPerMinute => ActiveMinutes > 0.5 ? Count.Mine / ActiveMinutes : null;
}

public sealed class MatchAnalyzer
{
    private readonly MatchFilter _filter;
    private readonly ChestClassifier _classifier;
    private readonly CountingOptions _options;
    private readonly IChestLocator? _locator;

    public MatchAnalyzer(MatchFilter? filter = null, ChestClassifier? classifier = null, CountingOptions? options = null, IChestLocator? locator = null)
    {
        _filter = filter ?? new MatchFilter();
        _classifier = classifier ?? ChestClassifier.Default;
        _options = options ?? new CountingOptions();
        _locator = locator;
    }

    public MatchSummary Analyze(MatchTrace trace)
    {
        var summary = new MatchSummary { Meta = trace.Meta };
        var resolver = new PlayerResolver(trace);
        summary.SkipReason = _filter.SkipReason(trace.Meta, resolver.HasOwner);
        if (summary.SkipReason != null)
        {
            return summary;
        }

        summary.Count = new MatchCounter(_classifier, _options, _locator).Count(trace);

        var mine = trace.PawnSamples.Where(s => resolver.Resolve(s.PawnId) == Who.Me).OrderBy(s => s.Time).ToList();
        if (mine.Count > 0)
        {
            var landing = FindLanding(mine);
            summary.LandingTime = landing.Time;
            summary.LandingX = landing.X;
            summary.LandingY = landing.Y;
            summary.ActiveEndTime = mine[^1].Time;
        }

        summary.FirstDeathTime = trace.Meta.OwnerDeathTime;
        summary.MineBeforeFirstDeath = summary.Count.Credits.Count(c => c.By == Who.Me && (summary.FirstDeathTime is not { } d || c.Time <= d));
        return summary;
    }

    /// <summary>Peak height above the touchdown point that marks a drop from the Battle Bus (≈600 m) rather than a
    /// jump on the pre-game island (15–35 m), Unreal units.</summary>
    public const double BusDropMinHeight = 15000;

    /// <summary>
    /// The drop point: first ground contact after an air phase that started high enough to be the Battle Bus drop.
    /// Pre-game island jumps (launch pads) are ignored. Falls back to the first ground contact after any air phase,
    /// then to the first sample.
    /// </summary>
    public static PawnSample FindLanding(IReadOnlyList<PawnSample> mine)
    {
        PawnSample? anyLanding = null;
        double peak = double.MinValue;
        var inAir = false;
        foreach (var s in mine)
        {
            if (s.InAir)
            {
                peak = inAir ? Math.Max(peak, s.Z) : s.Z;
                inAir = true;
                continue;
            }
            if (inAir)
            {
                anyLanding ??= s;
                if (peak - s.Z >= BusDropMinHeight)
                {
                    return s;
                }
            }
            inAir = false;
        }
        return anyLanding ?? mine[0];
    }
}
