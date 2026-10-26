namespace TimeSink.Core.Usage;

/// <summary>The absolute heat steps of a day total, shared by the Month calendar and the Week heat strip.</summary>
public static class HeatScale
{
    private static readonly long[] StepSeconds = [3600, 3 * 3600, 5 * 3600, 7 * 3600];

    /// <summary>Heat step 0-4 for a day total: under 1 h, 3 h, 5 h, 7 h, otherwise 7 h or more.</summary>
    public static int Level(long seconds)
    {
        for (var i = 0; i < StepSeconds.Length; i++)
            if (seconds < StepSeconds[i]) return i;
        return StepSeconds.Length;
    }
}
