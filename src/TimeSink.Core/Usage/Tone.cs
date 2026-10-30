namespace TimeSink.Core.Usage;

/// <summary>How a change is drawn (glossary: Tone). <see cref="Muted"/> is no change to compare with.</summary>
public enum Tone { Gain, Loss, Muted }

public static class Tones
{
    /// <summary>The tone of a change: a Gain at or above its baseline, judged before rounding (<see cref="Change.IsGain"/>), Muted when there is none.</summary>
    public static Tone Of(Change? change) => change is not { } c ? Tone.Muted : c.IsGain ? Tone.Gain : Tone.Loss;
}
