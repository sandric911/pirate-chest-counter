namespace PirateChest.Core.Updates;

/// <summary>Pure helpers for the self-updater (kept here so they are unit-tested).</summary>
public static class ReleaseFiles
{
    /// <summary>"v1.2.3" / "1.2.3" / "v1.2.3-beta" → 1.2.3; anything else → null.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return null;
        }
        var t = tag.Trim().TrimStart('v', 'V');
        var dash = t.IndexOf('-');
        if (dash >= 0)
        {
            t = t[..dash];
        }
        if (!Version.TryParse(t, out var v) || t.Count(c => c == '.') != 2)
        {
            return null;
        }
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    /// <summary>Expected SHA-256 (hex) of <paramref name="fileName"/> from a sha256sum-style list, or null.</summary>
    public static string? ExpectedHash(string sums, string fileName)
    {
        foreach (var raw in sums.Split('\n'))
        {
            var parts = raw.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[^1].TrimStart('*') == fileName && IsSha256(parts[0]))
            {
                return parts[0];
            }
        }
        return null;
    }

    public static bool IsSha256(string s) => s.Length == 64 && s.All(Uri.IsHexDigit);
}
