namespace TimeSink.Core.Theming;

/// <summary>The Lime/Blue/Pink accent presets (ui.accent). They override <c>accent</c> and the top of the heat ramp.</summary>
public static class Accents
{
    private static readonly Dictionary<string, (string Dark, string Light)> Presets = new()
    {
        ["Lime"] = ("#c6f26b", "#4f7a0a"),
        ["Blue"] = ("#6bb6ff", "#1f6fc4"),
        ["Pink"] = ("#ff8fc7", "#c2185b"),
    };

    public static Theme Apply(Theme theme, string? accent)
    {
        if (accent is null || !Presets.TryGetValue(accent, out var p)) return theme;
        var hex = theme.Id.EndsWith("light", StringComparison.Ordinal) ? p.Light : p.Dark;
        var colors = new Dictionary<string, string>(theme.Colors) { ["accent"] = hex };
        var heat = theme.Heat.ToList();
        heat[^1] = hex;
        return theme with { Colors = colors, Heat = heat };
    }
}
