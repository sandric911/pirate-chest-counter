using System.Globalization;
using Microsoft.Data.Sqlite;
using PirateChest.Core.Counting;

namespace PirateChest.App;

public sealed record FileRow(string Path, long Size, long MtimeTicks, string? ReplayId, string Status, string? Error, string? Build, string? AppVersion, int Attempts);

public sealed record MatchRow(
    string ReplayId, string FileName, string Status, string? SkipReason, string? Build, string? Playlist,
    DateTime StartedUtc, double LengthSec, int Mine, int Squad, int MineBroken, int SquadBroken, int MineHeuristic, int SquadHeuristic,
    int MineBeforeDeath, double? LandingTime, double? LandingX, double? LandingY, double? ActiveEnd, double? FirstDeath,
    int Foreign, int BrokenAfterOpen, int Unattributed, string ParserVersion, DateTime ProcessedUtc, int? Placement);

public sealed record CreditRow(string ReplayId, string ChestKey, string ClassName, string Type, int? Tier, string Kind, string By, string Method, double Time, double? X, double? Y, double? Z);

/// <summary>SQLite storage in %LOCALAPPDATA%\PirateChestCounter\data.db.</summary>
public sealed class Database
{
    private readonly string _cs;
    private readonly object _gate = new();

    public Database(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _cs = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWriteCreate}.ToString();
        Init();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_cs);
        c.Open();
        using var p = c.CreateCommand();
        p.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000;";
        p.ExecuteNonQuery();
        return c;
    }

    private void Init()
    {
        using var c = Open();
        Exec(c, """
            CREATE TABLE IF NOT EXISTS files(
                path TEXT PRIMARY KEY, size INTEGER, mtime INTEGER, replay_id TEXT, status TEXT, error TEXT,
                build TEXT, app_version TEXT, attempts INTEGER DEFAULT 0, updated_utc TEXT);
            CREATE TABLE IF NOT EXISTS matches(
                replay_id TEXT PRIMARY KEY, file_name TEXT, status TEXT, skip_reason TEXT, build TEXT, playlist TEXT,
                started_utc TEXT, length_sec REAL, mine INTEGER, squad INTEGER, mine_broken INTEGER, squad_broken INTEGER,
                mine_heur INTEGER, squad_heur INTEGER, mine_before_death INTEGER, landing_time REAL, landing_x REAL, landing_y REAL,
                active_end REAL, first_death REAL, foreign_chests INTEGER, broken_after_open INTEGER, unattributed INTEGER,
                parser_version TEXT, processed_utc TEXT, placement INTEGER);
            CREATE TABLE IF NOT EXISTS credits(
                replay_id TEXT, chest_key TEXT, class TEXT, type TEXT, tier INTEGER, kind TEXT, by_who TEXT, method TEXT,
                t REAL, x REAL, y REAL, z REAL);
            CREATE INDEX IF NOT EXISTS ix_credits_replay ON credits(replay_id);
            CREATE TABLE IF NOT EXISTS atlas(chest_key TEXT PRIMARY KEY, sx REAL, sy REAL, sz REAL, n INTEGER);
            CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT);
            CREATE TABLE IF NOT EXISTS fortnite_runs(id INTEGER PRIMARY KEY AUTOINCREMENT, start_utc TEXT, end_utc TEXT);
            """);
    }

    private static void Exec(SqliteConnection c, string sql, params (string, object?)[] ps)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in ps)
        {
            cmd.Parameters.AddWithValue(n, v ?? DBNull.Value);
        }
        cmd.ExecuteNonQuery();
    }

    private static string Iso(DateTime d) => d.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    private static DateTime ParseIso(string s) => DateTime.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);

    // ---------------------------------------------------------------- files (the processing queue)

    public FileRow? GetFile(string path)
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT path,size,mtime,replay_id,status,error,build,app_version,attempts FROM files WHERE path=$p";
            cmd.Parameters.AddWithValue("$p", path);
            using var r = cmd.ExecuteReader();
            return r.Read() ? ReadFile(r) : null;
        }
    }

    public List<FileRow> Files(string? status = null)
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT path,size,mtime,replay_id,status,error,build,app_version,attempts FROM files" + (status != null ? " WHERE status=$s" : "") + " ORDER BY path";
            if (status != null)
            {
                cmd.Parameters.AddWithValue("$s", status);
            }
            using var r = cmd.ExecuteReader();
            var list = new List<FileRow>();
            while (r.Read())
            {
                list.Add(ReadFile(r));
            }
            return list;
        }
    }

    private static FileRow ReadFile(SqliteDataReader r) => new(
        r.GetString(0), r.GetInt64(1), r.GetInt64(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetString(4),
        r.IsDBNull(5) ? null : r.GetString(5), r.IsDBNull(6) ? null : r.GetString(6), r.IsDBNull(7) ? null : r.GetString(7), r.IsDBNull(8) ? 0 : r.GetInt32(8));

    public void UpsertFile(string path, long size, long mtime, string? replayId, string status, string? error = null, string? build = null, bool incrementAttempts = false)
    {
        lock (_gate)
        {
            using var c = Open();
            Exec(c, """
                INSERT INTO files(path,size,mtime,replay_id,status,error,build,app_version,attempts,updated_utc)
                VALUES($p,$s,$m,$r,$st,$e,$b,$v,$a,$u)
                ON CONFLICT(path) DO UPDATE SET size=$s, mtime=$m, replay_id=COALESCE($r, replay_id), status=$st, error=$e,
                    build=COALESCE($b, build), app_version=$v, attempts=CASE WHEN $inc=1 THEN attempts+1 ELSE attempts END, updated_utc=$u
                """,
                ("$p", path), ("$s", size), ("$m", mtime), ("$r", replayId), ("$st", status), ("$e", error), ("$b", build),
                ("$v", AppInfo.VersionText), ("$a", incrementAttempts ? 1 : 0), ("$inc", incrementAttempts ? 1 : 0), ("$u", Iso(DateTime.UtcNow)));
        }
    }

    public void SetAllFilesStatus(string fromStatus, string toStatus)
    {
        lock (_gate)
        {
            using var c = Open();
            Exec(c, "UPDATE files SET status=$t WHERE status=$f", ("$t", toStatus), ("$f", fromStatus));
        }
    }

    public void RequeueAll()
    {
        lock (_gate)
        {
            using var c = Open();
            Exec(c, "UPDATE files SET status='pending', attempts=0 WHERE status IN ('done','error','skipped')");
        }
    }

    public void ForgetMissingFiles(IReadOnlySet<string> existing)
    {
        lock (_gate)
        {
            using var c = Open();
            var gone = new List<string>();
            using (var cmd = c.CreateCommand())
            {
                cmd.CommandText = "SELECT path FROM files";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    if (!existing.Contains(r.GetString(0)))
                    {
                        gone.Add(r.GetString(0));
                    }
                }
            }
            foreach (var p in gone)
            {
                Exec(c, "DELETE FROM files WHERE path=$p", ("$p", p));
            }
        }
    }

    // ---------------------------------------------------------------- matches

    public (string Status, string ParserVersion)? MatchState(string replayId)
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT status, parser_version FROM matches WHERE replay_id=$r";
            cmd.Parameters.AddWithValue("$r", replayId);
            using var r = cmd.ExecuteReader();
            return r.Read() ? (r.GetString(0), r.IsDBNull(1) ? "" : r.GetString(1)) : null;
        }
    }

    public void SaveMatch(string fileName, MatchSummary s)
    {
        lock (_gate)
        {
            using var c = Open();
            using var tx = c.BeginTransaction();
            var m = s.Meta;
            var cnt = s.Count;
            Exec(c, "DELETE FROM credits WHERE replay_id=$r", ("$r", m.ReplayId));
            Exec(c, """
                INSERT OR REPLACE INTO matches(replay_id,file_name,status,skip_reason,build,playlist,started_utc,length_sec,mine,squad,
                    mine_broken,squad_broken,mine_heur,squad_heur,mine_before_death,landing_time,landing_x,landing_y,active_end,first_death,
                    foreign_chests,broken_after_open,unattributed,parser_version,processed_utc,placement)
                VALUES($r,$f,$st,$sr,$b,$pl,$su,$len,$mine,$squad,$mb,$sb,$mh,$sh,$mbd,$lt,$lx,$ly,$ae,$fd,$fo,$bao,$un,$pv,$pu,$place)
                """,
                ("$r", m.ReplayId), ("$f", fileName), ("$st", s.Counted ? "counted" : "skipped"), ("$sr", s.SkipReason),
                ("$b", $"{m.BuildMajor}.{m.BuildMinor:00}"), ("$pl", m.Playlist), ("$su", Iso(m.TimestampUtc)), ("$len", m.LengthMs / 1000.0),
                ("$mine", cnt.Mine), ("$squad", cnt.MineAndSquad), ("$mb", cnt.MineBroken), ("$sb", cnt.SquadBroken),
                ("$mh", cnt.MineHeuristic), ("$sh", cnt.SquadHeuristic), ("$mbd", s.MineBeforeFirstDeath),
                ("$lt", s.LandingTime), ("$lx", s.LandingX), ("$ly", s.LandingY), ("$ae", s.ActiveEndTime), ("$fd", s.FirstDeathTime),
                ("$fo", cnt.ForeignChests), ("$bao", cnt.BrokenAfterOpen), ("$un", cnt.UnattributedDestroyed),
                ("$pv", AppInfo.ParserVersion), ("$pu", Iso(DateTime.UtcNow)), ("$place", m.OwnerPlacement));

            foreach (var cr in cnt.Credits)
            {
                Exec(c, "INSERT INTO credits(replay_id,chest_key,class,type,tier,kind,by_who,method,t,x,y,z) VALUES($r,$k,$c,$ty,$ti,$ki,$by,$me,$t,$x,$y,$z)",
                    ("$r", m.ReplayId), ("$k", cr.ChestKey), ("$c", cr.ClassName), ("$ty", cr.Type.ToString()), ("$ti", cr.LootTier),
                    ("$ki", cr.Kind.ToString()), ("$by", cr.By.ToString()), ("$me", cr.Method.ToString()), ("$t", (double)cr.Time),
                    ("$x", cr.X), ("$y", cr.Y), ("$z", cr.Z));
            }
            tx.Commit();
        }
    }

    public List<MatchRow> Matches()
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
                SELECT replay_id,file_name,status,skip_reason,build,playlist,started_utc,length_sec,mine,squad,mine_broken,squad_broken,
                       mine_heur,squad_heur,mine_before_death,landing_time,landing_x,landing_y,active_end,first_death,
                       foreign_chests,broken_after_open,unattributed,parser_version,processed_utc,placement
                FROM matches ORDER BY started_utc
                """;
            using var r = cmd.ExecuteReader();
            var list = new List<MatchRow>();
            while (r.Read())
            {
                double? D(int i) => r.IsDBNull(i) ? null : r.GetDouble(i);
                list.Add(new MatchRow(
                    r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetString(5), ParseIso(r.GetString(6)), r.GetDouble(7), r.GetInt32(8), r.GetInt32(9), r.GetInt32(10),
                    r.GetInt32(11), r.GetInt32(12), r.GetInt32(13), r.GetInt32(14), D(15), D(16), D(17), D(18), D(19),
                    r.GetInt32(20), r.GetInt32(21), r.GetInt32(22), r.IsDBNull(23) ? "" : r.GetString(23), ParseIso(r.GetString(24)),
                    r.IsDBNull(25) ? null : r.GetInt32(25)));
            }
            return list;
        }
    }

    public List<CreditRow> Credits(string? replayId = null)
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT replay_id,chest_key,class,type,tier,kind,by_who,method,t,x,y,z FROM credits" + (replayId != null ? " WHERE replay_id=$r" : "");
            if (replayId != null)
            {
                cmd.Parameters.AddWithValue("$r", replayId);
            }
            using var r = cmd.ExecuteReader();
            var list = new List<CreditRow>();
            while (r.Read())
            {
                double? D(int i) => r.IsDBNull(i) ? null : r.GetDouble(i);
                list.Add(new CreditRow(r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3), r.IsDBNull(4) ? null : r.GetInt32(4),
                    r.GetString(5), r.GetString(6), r.GetString(7), r.GetDouble(8), D(9), D(10), D(11)));
            }
            return list;
        }
    }

    // ---------------------------------------------------------------- atlas

    public ChestAtlas LoadAtlas()
    {
        var atlas = new ChestAtlas();
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT chest_key,sx,sy,sz,n FROM atlas";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var n = r.GetInt32(4);
                if (n > 0)
                {
                    atlas.Set(r.GetString(0), r.GetDouble(1) / n, r.GetDouble(2) / n, r.GetDouble(3) / n, n);
                }
            }
        }
        return atlas;
    }

    public void SaveAtlas(ChestAtlas atlas)
    {
        lock (_gate)
        {
            using var c = Open();
            using var tx = c.BeginTransaction();
            foreach (var (k, e) in atlas.Entries)
            {
                Exec(c, "INSERT OR REPLACE INTO atlas(chest_key,sx,sy,sz,n) VALUES($k,$x,$y,$z,$n)",
                    ("$k", k), ("$x", e.SumX), ("$y", e.SumY), ("$z", e.SumZ), ("$n", e.N));
            }
            tx.Commit();
        }
    }

    public int AtlasCount()
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM atlas";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }
    }

    // ---------------------------------------------------------------- settings

    public string? Get(string key)
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT value FROM settings WHERE key=$k";
            cmd.Parameters.AddWithValue("$k", key);
            return cmd.ExecuteScalar() as string;
        }
    }

    public void Set(string key, string? value)
    {
        lock (_gate)
        {
            using var c = Open();
            if (value == null)
            {
                Exec(c, "DELETE FROM settings WHERE key=$k", ("$k", key));
            }
            else
            {
                Exec(c, "INSERT OR REPLACE INTO settings(key,value) VALUES($k,$v)", ("$k", key), ("$v", value));
            }
        }
    }

    public double GetDouble(string key, double fallback) =>
        double.TryParse(Get(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;

    // ---------------------------------------------------------------- Fortnite runs

    public long FortniteStarted(DateTime utc)
    {
        lock (_gate)
        {
            using var c = Open();
            Exec(c, "INSERT INTO fortnite_runs(start_utc) VALUES($s)", ("$s", Iso(utc)));
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT last_insert_rowid()";
            return (long)cmd.ExecuteScalar()!;
        }
    }

    public void FortniteStopped(long id, DateTime utc)
    {
        lock (_gate)
        {
            using var c = Open();
            Exec(c, "UPDATE fortnite_runs SET end_utc=$e WHERE id=$i", ("$e", Iso(utc)), ("$i", id));
        }
    }

    public (DateTime Start, DateTime? End)? LastFortniteRun()
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT start_utc,end_utc FROM fortnite_runs ORDER BY id DESC LIMIT 1";
            using var r = cmd.ExecuteReader();
            if (!r.Read())
            {
                return null;
            }
            return (ParseIso(r.GetString(0)), r.IsDBNull(1) ? null : ParseIso(r.GetString(1)));
        }
    }
}
