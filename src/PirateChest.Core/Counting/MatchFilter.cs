using System.Text.RegularExpressions;
using PirateChest.Core.Trace;

namespace PirateChest.Core.Counting;

/// <summary>Rule 10: only BR / Zero Build matches of the Treasure Hunters mini-season (v43.x).</summary>
public sealed class MatchFilter
{
    public int[] AllowedBuildMajors { get; set; } = { 43 };
    public DateTime FromUtc { get; set; } = new(2026, 10, 31, 12, 0, 0, DateTimeKind.Utc);
    public DateTime ToUtc { get; set; } = new(2026, 12, 6, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Battle Royale and Zero Build playlists (normal and ranked) on the main island.</summary>
    public string[] PlaylistPatterns { get; set; } =
    {
        @"^Playlist_Default(Solo|Duo|Trio|Squad)$",
        @"^Playlist_NoBuildBR_(Solo|Duo|Trio|Squad)$",
        @"^Playlist_(Showdown|ShowdownAlt|Habanero)\w*$",
        @"^Playlist_NoBuildBR_Habanero\w*$",
    };

    /// <summary>Returns null when the match counts, otherwise a human readable (Russian) reason.</summary>
    public string? SkipReason(ReplayMeta meta, bool hasOwner)
    {
        if (meta.IsLive)
        {
            return "Реплей ещё записывается";
        }
        if (!AllowedBuildMajors.Contains(meta.BuildMajor))
        {
            return $"Билд {meta.BuildMajor}.{meta.BuildMinor:00} — не мини-сезон Treasure Hunters (нужен v43.x)";
        }
        if (meta.TimestampUtc < FromUtc)
        {
            return $"Матч {meta.TimestampUtc:dd.MM.yyyy} — раньше 1 ноября 2026";
        }
        if (meta.TimestampUtc >= ToUtc)
        {
            return $"Матч {meta.TimestampUtc:dd.MM.yyyy} — после конца мини-сезона (5 декабря)";
        }
        if (string.IsNullOrEmpty(meta.Playlist))
        {
            return "Режим не определён (матч слишком короткий или реплей неполный)";
        }
        if (!PlaylistPatterns.Any(p => Regex.IsMatch(meta.Playlist, p, RegexOptions.IgnoreCase)))
        {
            return $"Режим {meta.Playlist} — не Battle Royale / Zero Build";
        }
        if (!hasOwner)
        {
            return "Не удалось определить владельца реплея";
        }
        return null;
    }

    /// <summary>Short Russian label for a playlist.</summary>
    public static string ModeLabel(string? playlist) => ModeLabel(playlist, english: false);

    /// <summary>Short label for a playlist, in Russian or English.</summary>
    public static string ModeLabel(string? playlist, bool english)
    {
        if (string.IsNullOrEmpty(playlist))
        {
            return "—";
        }
        bool Ends(params string[] s) => s.Any(x => playlist.EndsWith(x, StringComparison.OrdinalIgnoreCase));
        var nb = playlist.Contains("NoBuild", StringComparison.OrdinalIgnoreCase);
        var size = Ends("Solo") ? (english ? "solo" : "соло")
            : Ends("Duo", "Duos") ? (english ? "duos" : "дуо")
            : Ends("Trio", "Trios") ? (english ? "trios" : "трио")
            : Ends("Squad", "Squads") ? (english ? "squads" : "отряды")
            : playlist.Replace("Playlist_", "");
        var ranked = playlist.Contains("Habanero", StringComparison.OrdinalIgnoreCase) || playlist.Contains("Showdown", StringComparison.OrdinalIgnoreCase);
        var mode = nb ? "Zero Build" : english ? "Battle Royale" : "Королевская битва";
        return $"{mode}{(ranked ? (english ? " (ranked)" : " (рейтинг)") : "")}, {size}";
    }
}
