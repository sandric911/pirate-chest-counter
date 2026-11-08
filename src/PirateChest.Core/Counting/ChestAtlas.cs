using PirateChest.Core.Trace;

namespace PirateChest.Core.Counting;

/// <summary>
/// Locations of static chests, learned across matches. Static (level-placed) chests carry no position
/// in the replay, but their actor name (…_UAID_…) is stable between matches on the same map, and every
/// search reveals the searcher's position (within ~2 m of the chest). Averaging those gives the chest.
/// </summary>
public sealed class ChestAtlas : IChestLocator
{
    public sealed class Entry
    {
        public double SumX { get; set; }
        public double SumY { get; set; }
        public double SumZ { get; set; }
        public int N { get; set; }
        public double X => SumX / N;
        public double Y => SumY / N;
        public double Z => SumZ / N;
    }

    private readonly Dictionary<string, Entry> _entries = new();

    public IReadOnlyDictionary<string, Entry> Entries => _entries;

    public int MinSamples { get; set; } = 1;

    public void Add(string key, double x, double y, double z)
    {
        if (!_entries.TryGetValue(key, out var e))
        {
            e = new Entry();
            _entries[key] = e;
        }
        // Reject outliers once we have a decent estimate (searcher was gliding by, etc.).
        if (e.N >= 3)
        {
            var dx = x - e.X;
            var dy = y - e.Y;
            if (dx * dx + dy * dy > 800 * 800)
            {
                return;
            }
        }
        e.SumX += x;
        e.SumY += y;
        e.SumZ += z;
        e.N++;
    }

    public void Set(string key, double x, double y, double z, int n) =>
        _entries[key] = new Entry { SumX = x * n, SumY = y * n, SumZ = z * n, N = n };

    /// <summary>Learns from opens (not breaks: a breaker may stand far away).</summary>
    public void Learn(MatchTrace trace)
    {
        foreach (var e in trace.ContainerEvents)
        {
            if (e.StaticName != null && e.AlreadySearched == true && e.SearcherX is { } x && e.SearcherY is { } y)
            {
                Add(e.StaticName, x, y, e.SearcherZ ?? 0);
            }
        }
    }

    public (double X, double Y, double Z)? Locate(string chestKey) =>
        _entries.TryGetValue(chestKey, out var e) && e.N >= MinSamples ? (e.X, e.Y, e.Z) : null;
}
