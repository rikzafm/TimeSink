namespace TimeSink.Core.Usage;

/// <summary>
/// How much an amount changed against a baseline, as a percentage (+25 = a quarter above, -50 = half). It exists only
/// when the baseline is above zero: "new" and "nothing to compare with" are the absence of a Change, never infinity.
/// The unit lives here so a fraction can never be shown where a percentage is expected.
/// </summary>
public readonly record struct Change
{
    private Change(double percent) => Percent = percent;

    /// <summary>The change in percent: (current - baseline) / baseline * 100.</summary>
    public double Percent { get; }

    /// <summary>At or above the baseline (the views draw it as a gain). Judged before rounding, so -0.3% is not a gain.</summary>
    public bool IsGain => Percent >= 0;

    /// <summary>The change of <paramref name="current"/> against <paramref name="baseline"/>; null when the baseline is 0 or less.</summary>
    public static Change? Between(double current, double baseline)
    {
        if (!(baseline > 0)) return null;
        var percent = (current - baseline) * 100.0 / baseline;
        return double.IsFinite(percent) ? new Change(percent) : null;
    }
}
