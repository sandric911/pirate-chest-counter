using System.Text.Json;
using PirateChest.Core.Counting;

namespace PirateChest.App;

public sealed class Poi
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Radius in Unreal units.</summary>
    public double R { get; set; } = 25000;
}

/// <summary>Aggregations for the web UI. Everything is computed from the database on request.</summary>
public sealed class Stats
{
    public static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(45);
    private readonly Database _db;

    public Stats(Database db) => _db = db;

    private static DateTime Local(DateTime utc) => utc.ToLocalTime();

    public static string TypeLabel(string t) => t switch
    {
        nameof(ChestType.Normal) => "Обычные",
        nameof(ChestType.Rare) => "Редкие",
        nameof(ChestType.Sprite) => "Спрайтовые",
        _ => t,
    };

    /// <summary>Matches whose mode could not be determined (30-second leftovers etc.) are not shown anywhere.</summary>
    public static bool IsHidden(MatchRow m) => m.Status == "skipped" && string.IsNullOrEmpty(m.Playlist);

    public object Summary(ProcessorStatus ps)
    {
        var matches = _db.Matches().Where(m => !IsHidden(m)).ToList();
        var counted = matches.Where(m => m.Status == "counted").ToList();
        var credits = _db.Credits();
        var countedIds = counted.Select(m => m.ReplayId).ToHashSet();
        credits = credits.Where(c => countedIds.Contains(c.ReplayId)).ToList();
        var mine = credits.Where(c => c.By == nameof(Who.Me)).ToList();
        var squad = credits.Where(c => c.By is nameof(Who.Me) or nameof(Who.Teammate)).ToList();

        var today = DateTime.Now.Date;
        var todayIds = counted.Where(m => Local(m.StartedUtc).Date == today).Select(m => m.ReplayId).ToHashSet();
        var sessionIds = CurrentSession(counted).Select(m => m.ReplayId).ToHashSet();

        var types = new[] { nameof(ChestType.Normal), nameof(ChestType.Rare), nameof(ChestType.Sprite) };
        var last = counted.LastOrDefault();

        return new
        {
            mine = mine.Count,
            squad = squad.Count,
            today = new { mine = mine.Count(c => todayIds.Contains(c.ReplayId)), squad = squad.Count(c => todayIds.Contains(c.ReplayId)), matches = todayIds.Count },
            session = new { mine = mine.Count(c => sessionIds.Contains(c.ReplayId)), squad = squad.Count(c => sessionIds.Contains(c.ReplayId)), matches = sessionIds.Count },
            byType = types.Select(t => new
            {
                type = t,
                label = TypeLabel(t),
                mine = mine.Count(c => c.Type == t),
                squad = squad.Count(c => c.Type == t),
            }),
            best = counted.Count > 0 ? counted.Max(m => m.Mine) : 0,
            perMatch = counted.Count > 0 ? Math.Round(counted.Average(m => m.Mine), 1) : 0,
            heuristic = new { mine = mine.Count(c => c.Method == nameof(Attribution.Heuristic)), squad = squad.Count(c => c.Method == nameof(Attribution.Heuristic)) },
            matches = counted.Count,
            skipped = matches.Count(m => m.Status == "skipped"),
            lastMatch = last == null ? null : new
            {
                startedUtc = last.StartedUtc,
                endedUtc = last.StartedUtc.AddSeconds(last.LengthSec),
                mine = last.Mine,
                squad = last.Squad,
                mode = MatchFilter.ModeLabel(last.Playlist),
            },
            progress = new { busy = ps.Current != null || ps.Pending > 0, current = ps.Current, done = ps.BatchDone, total = ps.BatchTotal, pending = ps.Pending, live = ps.Live },
            version = AppInfo.VersionText,
        };
    }

    /// <summary>Matches of the current gaming session: consecutive matches with gaps under 45 min, ending recently.</summary>
    public static List<MatchRow> CurrentSession(List<MatchRow> counted)
    {
        var result = new List<MatchRow>();
        if (counted.Count == 0)
        {
            return result;
        }
        var ordered = counted.OrderBy(m => m.StartedUtc).ToList();
        var lastEnd = ordered[^1].StartedUtc.AddSeconds(ordered[^1].LengthSec);
        if (DateTime.UtcNow - lastEnd > SessionGap)
        {
            return result; // no session in progress
        }
        result.Add(ordered[^1]);
        for (var i = ordered.Count - 2; i >= 0; i--)
        {
            var end = ordered[i].StartedUtc.AddSeconds(ordered[i].LengthSec);
            if (ordered[i + 1].StartedUtc - end > SessionGap)
            {
                break;
            }
            result.Add(ordered[i]);
        }
        return result;
    }

    public object MatchList()
    {
        var matches = _db.Matches().Where(m => !IsHidden(m)).ToList();
        return matches.OrderByDescending(m => m.StartedUtc).Select(m => new
        {
            id = m.ReplayId,
            file = m.FileName,
            status = m.Status,
            skipReason = m.SkipReason,
            startedUtc = m.StartedUtc,
            mode = MatchFilter.ModeLabel(m.Playlist),
            playlist = m.Playlist,
            build = m.Build,
            durationMin = Math.Round(m.LengthSec / 60.0, 1),
            mine = m.Mine,
            squad = m.Squad,
            mineHeuristic = m.MineHeuristic,
            squadHeuristic = m.SquadHeuristic,
            placement = m.Placement,
            unattributed = m.Unattributed,
        });
    }

    public object Errors()
    {
        return _db.Files().Where(f => f.Status is "error" or "live" or "pending").Select(f => new
        {
            file = Path.GetFileName(f.Path),
            status = f.Status switch { "error" => "ошибка", "live" => "идёт запись", _ => "в очереди" },
            error = f.Error,
            build = f.Build,
            attempts = f.Attempts,
            appVersion = f.AppVersion,
        });
    }

    public object Daily()
    {
        var counted = _db.Matches().Where(m => m.Status == "counted").ToList();
        var days = counted.GroupBy(m => Local(m.StartedUtc).Date).OrderBy(g => g.Key).ToList();
        int cm = 0, cs = 0;
        return days.Select(g =>
        {
            var mine = g.Sum(m => m.Mine);
            var squad = g.Sum(m => m.Squad);
            cm += mine;
            cs += squad;
            return new { date = g.Key.ToString("yyyy-MM-dd"), mine, squad, matches = g.Count(), cumMine = cm, cumSquad = cs };
        }).ToList();
    }

    public List<Poi> Pois()
    {
        var list = LoadEmbeddedPois();
        var names = _db.Get("poi_names");
        if (names != null)
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(names) ?? new();
            foreach (var p in list)
            {
                if (map.TryGetValue(p.Id, out var n) && !string.IsNullOrWhiteSpace(n))
                {
                    p.Name = n;
                }
            }
        }
        return list;
    }

    private static List<Poi> LoadEmbeddedPois()
    {
        using var s = typeof(Stats).Assembly.GetManifestResourceStream("pois.json");
        if (s == null)
        {
            return new();
        }
        return JsonSerializer.Deserialize<List<Poi>>(s, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
    }

    public void RenamePoi(string id, string name)
    {
        var map = _db.Get("poi_names") is { } j ? JsonSerializer.Deserialize<Dictionary<string, string>>(j) ?? new() : new();
        map[id] = name.Trim();
        _db.Set("poi_names", JsonSerializer.Serialize(map));
    }

    /// <summary>POI whose radius covers the point; else "рядом с POI" within 2.5 radii; else a 300 m grid district.</summary>
    public static (string Id, string Name, bool IsPoi) PlaceOf(double x, double y, List<Poi> pois)
    {
        Poi? best = null;
        var bestRel = double.MaxValue;
        foreach (var p in pois)
        {
            var rel = Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y)) / Math.Max(1, p.R);
            if (rel < bestRel)
            {
                best = p;
                bestRel = rel;
            }
        }
        if (best != null && bestRel <= 1)
        {
            return (best.Id, best.Name, true);
        }
        if (best != null && bestRel <= 2.5)
        {
            return ("near:" + best.Id, "Рядом с " + best.Name, false);
        }
        if (pois.Count > 0 && pois.Min(p => Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y))) > 60000)
        {
            return ("none", "Не высадился", false); // e.g. left on the pre-game island: not a drop spot
        }
        const double cell = 30000;
        var cx = Math.Round(x / cell);
        var cy = Math.Round(y / cell);
        return ($"cell:{cx}:{cy}", $"Район ({cx * cell / 100:0}, {cy * cell / 100:0}) м", false);
    }

    public object Efficiency()
    {
        var counted = _db.Matches().Where(m => m.Status == "counted").ToList();
        var pois = Pois();
        var rows = counted.Select(m =>
        {
            var minutes = m.LandingTime is { } l && m.ActiveEnd is { } e && e > l ? (e - l) / 60.0 : 0;
            var place = m.LandingX is { } x && m.LandingY is { } y ? PlaceOf(x, y, pois) : ("none", "—", false);
            return new
            {
                id = m.ReplayId,
                startedUtc = m.StartedUtc,
                mode = MatchFilter.ModeLabel(m.Playlist),
                mine = m.Mine,
                beforeDeath = m.MineBeforeDeath,
                minutes = Math.Round(minutes, 1),
                cpm = minutes > 0.5 ? Math.Round(m.Mine / minutes, 2) : (double?)null,
                landingX = m.LandingX,
                landingY = m.LandingY,
                placeId = place.Item1,
                place = place.Item2,
                isPoi = place.Item3,
            };
        }).ToList();

        var withTime = rows.Where(r => r.cpm != null).ToList();
        var totalMinutes = withTime.Sum(r => r.minutes);
        var ranking = rows.Where(r => r.placeId != "none").GroupBy(r => r.placeId).Select(g => new
        {
            id = g.Key,
            name = g.First().place,
            isPoi = g.First().isPoi,
            matches = g.Count(),
            avgMine = Math.Round(g.Average(r => r.mine), 1),
            avgBeforeDeath = Math.Round(g.Average(r => r.beforeDeath), 1),
            cpm = g.Sum(r => r.minutes) > 0 ? Math.Round(g.Sum(r => r.mine) / g.Sum(r => r.minutes), 2) : 0,
            best = g.Max(r => r.mine),
            x = g.Average(r => r.landingX ?? 0),
            y = g.Average(r => r.landingY ?? 0),
        }).OrderByDescending(r => r.cpm).ThenByDescending(r => r.avgMine).ToList();

        return new
        {
            overall = new
            {
                matches = rows.Count,
                cpm = totalMinutes > 0 ? Math.Round(withTime.Sum(r => r.mine) / totalMinutes, 2) : 0,
                perMatch = rows.Count > 0 ? Math.Round(rows.Average(r => r.mine), 1) : 0,
                beforeDeath = rows.Count > 0 ? Math.Round(rows.Average(r => r.beforeDeath), 1) : 0,
                best = rows.Count > 0 ? rows.Max(r => r.mine) : 0,
            },
            matches = rows.OrderByDescending(r => r.startedUtc),
            ranking,
        };
    }

    public object MapPoints(string? matchId)
    {
        var countedIds = _db.Matches().Where(m => m.Status == "counted").Select(m => m.ReplayId).ToHashSet();
        var credits = _db.Credits(matchId).Where(c => countedIds.Contains(c.ReplayId) && c.X != null && c.Y != null);
        return credits.Select(c => new
        {
            m = c.ReplayId,
            x = Math.Round(c.X!.Value),
            y = Math.Round(c.Y!.Value),
            by = c.By == nameof(Who.Me) ? "me" : "squad",
            heur = c.Method == nameof(Attribution.Heuristic),
            type = c.Type,
        });
    }

    public object AtlasPoints(ChestAtlas atlas)
    {
        lock (atlas)
        {
            return atlas.Entries.Where(e => e.Value.N >= 1).Select(e => new { x = Math.Round(e.Value.X), y = Math.Round(e.Value.Y) }).ToList();
        }
    }
}
