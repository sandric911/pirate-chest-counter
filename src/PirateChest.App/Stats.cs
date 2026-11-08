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
    /// <summary>A break between matches longer than this ends the gaming session.</summary>
    public static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(60);
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

    public object Summary(ProcessorStatus ps, bool fortniteRunning)
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
        var now = DateTime.UtcNow;
        var session = CurrentSession(counted, fortniteRunning ? ps.LiveSinceUtc : null, now);
        var sessionIds = session.Matches.Select(m => m.ReplayId).ToHashSet();

        var types = new[] { nameof(ChestType.Normal), nameof(ChestType.Rare), nameof(ChestType.Sprite) };
        var last = counted.LastOrDefault();

        return new
        {
            mine = mine.Count,
            squad = squad.Count,
            today = new { mine = mine.Count(c => todayIds.Contains(c.ReplayId)), squad = squad.Count(c => todayIds.Contains(c.ReplayId)), matches = todayIds.Count },
            session = new
            {
                active = session.StartedUtc != null,
                startedUtc = session.StartedUtc,
                lastEndUtc = session.LastEndUtc,
                playing = session.Playing,
                gapMin = (int)SessionGap.TotalMinutes,
                serverNowUtc = now,
                mine = mine.Count(c => sessionIds.Contains(c.ReplayId)),
                squad = squad.Count(c => sessionIds.Contains(c.ReplayId)),
                matches = sessionIds.Count,
            },
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

    public sealed record SessionInfo(List<MatchRow> Matches, DateTime? StartedUtc, DateTime? LastEndUtc, bool Playing);

    /// <summary>
    /// The current gaming session: consecutive matches with breaks of at most <see cref="SessionGap"/>.
    /// It goes on while the break since the last match is within the gap; a match being recorded right now
    /// (<paramref name="recordingSinceUtc"/>) ends the break, or starts a new session if the old one is over.
    /// </summary>
    public static SessionInfo CurrentSession(List<MatchRow> counted, DateTime? recordingSinceUtc, DateTime nowUtc)
    {
        static DateTime End(MatchRow m) => m.StartedUtc.AddSeconds(m.LengthSec);
        var ordered = counted.OrderBy(m => m.StartedUtc).ToList();
        var chain = new List<MatchRow>();
        if (ordered.Count > 0 && (recordingSinceUtc ?? nowUtc) - End(ordered[^1]) <= SessionGap)
        {
            chain.Add(ordered[^1]);
            for (var i = ordered.Count - 2; i >= 0; i--)
            {
                if (ordered[i + 1].StartedUtc - End(ordered[i]) > SessionGap)
                {
                    break;
                }
                chain.Add(ordered[i]);
            }
            chain.Reverse();
        }
        var playing = recordingSinceUtc != null;
        if (chain.Count == 0 && !playing)
        {
            return new SessionInfo(chain, null, null, false);
        }
        var started = chain.Count > 0 ? chain[0].StartedUtc : recordingSinceUtc!.Value;
        if (recordingSinceUtc is { } r && r < started)
        {
            started = r;
        }
        return new SessionInfo(chain, started, chain.Count > 0 ? End(chain[^1]) : null, playing);
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
                best = rows.Count > 0 ? rows.Max(r => r.mine) : 0,
            },
            matches = rows.OrderByDescending(r => r.startedUtc),
            ranking,
        };
    }

    private sealed class ChestSpot
    {
        public double SumX, SumY;
        public int N;
        public bool Fixed;
        public bool Dynamic;
        public string Type = "";
        public int Me, Squad;
        public readonly Dictionary<string, int> Types = new();
        public readonly HashSet<string> MeMatches = new();
        public double X => SumX / Math.Max(1, N);
        public double Y => SumY / Math.Max(1, N);
    }

    /// <summary>
    /// One marker per chest with how many times I and my teammates searched it. Level-placed chests are matched
    /// across matches by their actor name (stable on this map) and placed by the atlas; spawned chests (relic,
    /// buried, booty) by type and position within 3 m. <c>others</c>: chests the atlas knows that none of these
    /// searches touched.
    /// </summary>
    public object MapChests(string? matchId, ChestAtlas atlas)
    {
        const double same = 300; // 3 m
        var countedIds = _db.Matches().Where(m => m.Status == "counted").Select(m => m.ReplayId).ToHashSet();
        var credits = _db.Credits(matchId)
            .Where(c => countedIds.Contains(c.ReplayId) && c.By is nameof(Who.Me) or nameof(Who.Teammate))
            .OrderBy(c => c.ReplayId).ThenBy(c => c.Time).ToList();
        var spots = new List<ChestSpot>();
        var byKey = new Dictionary<string, ChestSpot>();
        var classifier = ChestClassifier.Default;
        lock (atlas)
        {
            foreach (var c in credits)
            {
                ChestSpot? spot;
                var dynamic = c.ChestKey.StartsWith("dyn:", StringComparison.Ordinal);
                if (!dynamic)
                {
                    if (!byKey.TryGetValue(c.ChestKey, out spot))
                    {
                        spot = new ChestSpot();
                        if (atlas.Locate(c.ChestKey) is { } at)
                        {
                            (spot.SumX, spot.SumY, spot.N, spot.Fixed) = (at.X, at.Y, 1, true);
                        }
                        byKey[c.ChestKey] = spot;
                        spots.Add(spot);
                    }
                }
                else
                {
                    if (c.X is not { } dx || c.Y is not { } dy)
                    {
                        continue;
                    }
                    spot = spots.FirstOrDefault(s => s.Dynamic && s.Types.ContainsKey(c.Type)
                        && (s.X - dx) * (s.X - dx) + (s.Y - dy) * (s.Y - dy) <= same * same);
                    if (spot == null)
                    {
                        spot = new ChestSpot { Dynamic = true };
                        spots.Add(spot);
                    }
                }
                if (!spot.Fixed && c.X is { } x && c.Y is { } y)
                {
                    spot.SumX += x;
                    spot.SumY += y;
                    spot.N++;
                }
                spot.Types[c.Type] = spot.Types.GetValueOrDefault(c.Type) + 1;
                if (c.By == nameof(Who.Me))
                {
                    spot.Me++;
                    spot.MeMatches.Add(c.ReplayId);
                }
                else
                {
                    spot.Squad++;
                }
            }

            var others = atlas.Entries
                .Where(e => !byKey.ContainsKey(e.Key) && e.Value.N >= atlas.MinSamples && IsChestKey(e.Key, classifier))
                .Select(e => new { x = Math.Round(e.Value.X), y = Math.Round(e.Value.Y) })
                .ToList();

            return new
            {
                chests = spots.Where(s => s.N > 0).Select(s => new
                {
                    x = Math.Round(s.X),
                    y = Math.Round(s.Y),
                    type = s.Types.OrderByDescending(t => t.Value).ThenByDescending(t => Rarity(t.Key)).First().Key,
                    me = s.Me,
                    squad = s.Squad,
                    matches = s.MeMatches.Count,
                }).ToList(),
                others,
            };
        }
    }

    private static int Rarity(string type) => type switch { nameof(ChestType.Sprite) => 2, nameof(ChestType.Rare) => 1, _ => 0 };

    /// <summary>Atlas keys are actor names such as <c>Tiered_Chest_Athena_C_UAID_…</c>; ammo boxes and the like are skipped.</summary>
    private static bool IsChestKey(string key, ChestClassifier classifier)
    {
        var end = key.IndexOf("_C_", StringComparison.Ordinal);
        var cls = end > 0 ? key[..(end + 2)] : key;
        return classifier.RoleOf(cls, out _) == ContainerRole.Chest;
    }
}
