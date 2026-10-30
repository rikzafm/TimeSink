using TimeSink.Core.Usage;

namespace TimeSink.UI.Services;

/// <summary>The theme brush a <see cref="Tone"/> is drawn in; the one place a view maps a change to a colour.</summary>
internal static class ToneBrush
{
    public static string Key(this Tone tone) => tone switch
    {
        Tone.Gain => "Brush.Accent",
        Tone.Loss => "Brush.Warning",
        _ => "Brush.TextMuted",
    };
}
