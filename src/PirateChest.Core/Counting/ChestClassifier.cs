using System.Text.Json;
using System.Text.Json.Serialization;

namespace PirateChest.Core.Counting;

public enum ChestType
{
    /// <summary>Обычный.</summary>
    Normal,
    /// <summary>Редкий.</summary>
    Rare,
    /// <summary>Treasure Hunt (золотой, с черепом).</summary>
    TreasureHunt,
    /// <summary>Другой (закопанный, реликвийный и т.п.).</summary>
    Other,
}

public enum ContainerRole
{
    /// <summary>A chest that counts.</summary>
    Chest,
    /// <summary>Treasure Hunt animated chest: marks its SourceContainer as Treasure Hunt, never counted itself.</summary>
    TreasureMarker,
    /// <summary>Ammo boxes, food boxes, props, gameplay actors: never counted.</summary>
    Ignored,
}

public sealed class ChestClassRule
{
    /// <summary>Exact short class name, or a prefix ending with '*'.</summary>
    public string Match { get; set; } = "";
    public ContainerRole Role { get; set; }
    public ChestType Type { get; set; }
    /// <summary>If set, a loot tier &gt;= this value upgrades the type to Rare.</summary>
    public int? RareFromTier { get; set; }

    public bool Matches(string className) =>
        Match.EndsWith('*')
            ? className.StartsWith(Match[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(className, Match, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Decides which replicated container classes are chests and what type they are.
/// Rules live in an embedded JSON (chest-classes.json) so a Fortnite update that adds
/// a new chest class can be fixed without touching code.
/// </summary>
public sealed class ChestClassifier
{
    private readonly List<ChestClassRule> _rules;

    public ChestClassifier(IEnumerable<ChestClassRule> rules) => _rules = rules.ToList();

    public static ChestClassifier Default { get; } = LoadDefault();

    public IReadOnlyList<ChestClassRule> Rules => _rules;

    public static ChestClassifier LoadDefault()
    {
        using var s = typeof(ChestClassifier).Assembly.GetManifestResourceStream("PirateChest.Core.chest-classes.json")
            ?? throw new InvalidOperationException("chest-classes.json is not embedded");
        return FromJson(s);
    }

    public static ChestClassifier FromJson(Stream json)
    {
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, Converters = { new JsonStringEnumConverter() } };
        var rules = JsonSerializer.Deserialize<List<ChestClassRule>>(json, opts) ?? new();
        return new ChestClassifier(rules);
    }

    /// <summary>
    /// Role of a class. Unknown classes whose name contains "Chest" are treated as chests of type
    /// Other (they only ever count if a real search event happens on them); everything else is ignored.
    /// </summary>
    public ContainerRole RoleOf(string className, out ChestClassRule? rule)
    {
        rule = _rules.FirstOrDefault(r => r.Matches(className));
        if (rule != null)
        {
            return rule.Role;
        }
        return className.Contains("Chest", StringComparison.OrdinalIgnoreCase) ? ContainerRole.Chest : ContainerRole.Ignored;
    }

    public bool IsKnown(string className) => _rules.Any(r => r.Matches(className));

    public ChestType TypeOf(string className, int? lootTier, bool treasure)
    {
        if (treasure)
        {
            return ChestType.TreasureHunt;
        }
        RoleOf(className, out var rule);
        if (rule == null)
        {
            return ChestType.Other;
        }
        if (rule.RareFromTier is { } t && lootTier >= t)
        {
            return ChestType.Rare;
        }
        return rule.Type;
    }
}
