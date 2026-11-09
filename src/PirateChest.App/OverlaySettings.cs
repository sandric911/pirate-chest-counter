using System.Text.Json;

namespace PirateChest.App;

/// <summary>How the chest counter overlay looks and when it shows. Stored as JSON in settings.overlay.</summary>
public sealed class OverlaySettings
{
    public const string Key = "overlay";

    /// <summary>On by default (the author's choice): only the number, yellow, no background, while Fortnite is active.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>total | session | today</summary>
    public string Show { get; set; } = "total";
    /// <summary>Top-left corner in screen pixels; null = top-right corner of the main screen.</summary>
    public int? X { get; set; }
    public int? Y { get; set; }
    /// <summary>Size in percent, 50…300.</summary>
    public int Scale { get; set; } = 75;
    /// <summary>Number colour as #rrggbb.</summary>
    public string Color { get; set; } = "#f6d24a";
    /// <summary>none | dark | glass</summary>
    public string Background { get; set; } = "glass";
    /// <summary>Background opacity in percent, 0…100.</summary>
    public int Opacity { get; set; }
    public bool Icon { get; set; }
    public bool Label { get; set; }
    /// <summary>fortnite (only while the Fortnite window is the active one) | always</summary>
    public string When { get; set; } = "fortnite";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static OverlaySettings Load(Database db)
    {
        try
        {
            if (db.Get(Key) is { } j)
            {
                return (JsonSerializer.Deserialize<OverlaySettings>(j, Json) ?? new OverlaySettings()).Normalized();
            }
        }
        catch (Exception ex)
        {
            Log.Error("overlay settings", ex);
        }
        return new OverlaySettings();
    }

    public void Save(Database db) => db.Set(Key, JsonSerializer.Serialize(Normalized(), Json));

    public OverlaySettings Normalized()
    {
        Show = Show is "session" or "today" ? Show : "total";
        Scale = Math.Clamp(Scale, 50, 300);
        Opacity = Math.Clamp(Opacity, 0, 100);
        Background = Background is "none" or "glass" ? Background : "dark";
        When = When == "always" ? "always" : "fortnite";
        if (Color is not { Length: 7 } || Color[0] != '#' || !int.TryParse(Color.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _))
        {
            Color = "#ffffff";
        }
        return this;
    }

    public OverlaySettings Clone() => (OverlaySettings)MemberwiseClone();
}
