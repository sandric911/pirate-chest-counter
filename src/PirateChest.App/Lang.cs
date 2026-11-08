using System.Text.RegularExpressions;

namespace PirateChest.App;

/// <summary>Texts the program itself produces: mode and place names, warnings, update status, tray texts.</summary>
public static class Lang
{
    /// <summary>The interface is English only (Russian was dropped at the author's request). Russian strings are kept
    /// next to the English ones in the code for reference, and skip reasons stored in Russian are translated on display.</summary>
    public static bool En => true;

    public static string T(string ru, string en) => En ? en : ru;

    /// <summary>"сундук/сундука/сундуков" or "chest/chests".</summary>
    public static string Chests(int k)
    {
        if (En)
        {
            return k == 1 ? "chest" : "chests";
        }
        var m10 = k % 10;
        var m100 = k % 100;
        return m10 == 1 && m100 != 11 ? "сундук" : m10 is >= 2 and <= 4 && (m100 < 12 || m100 > 14) ? "сундука" : "сундуков";
    }

    private static readonly (Regex Ru, string En)[] SkipReasons =
    {
        (new Regex("^Реплей ещё записывается$"), "The replay is still being recorded"),
        (new Regex(@"^Билд (\S+) — не мини-сезон Treasure Hunters \(нужен v43\.x\)$"), "Build $1 is not the Treasure Hunters mini-season (v43.x needed)"),
        (new Regex(@"^Матч (\S+) — раньше 1 ноября 2026$"), "Match of $1 is before 1 November 2026"),
        (new Regex(@"^Матч (\S+) — после конца мини-сезона \(5 декабря\)$"), "Match of $1 is after the end of the mini-season (5 December)"),
        (new Regex(@"^Режим не определён.*$"), "Mode unknown (match too short or replay incomplete)"),
        (new Regex(@"^Режим (\S+) — не Battle Royale / Zero Build$"), "Mode $1 is not Battle Royale / Zero Build"),
        (new Regex("^Не удалось определить владельца реплея$"), "Could not find the replay owner"),
    };

    /// <summary>Skip reasons are stored in Russian; shown in English when that language is on.</summary>
    public static string? SkipReason(string? ru)
    {
        if (!En || string.IsNullOrEmpty(ru))
        {
            return ru;
        }
        foreach (var (re, en) in SkipReasons)
        {
            if (re.IsMatch(ru))
            {
                return re.Replace(ru, en);
            }
        }
        return ru;
    }
}

/// <summary>An error whose message exists in both interface languages.</summary>
public sealed class LocalizedException(string ru, string en) : Exception(ru)
{
    public string En => en;
}
