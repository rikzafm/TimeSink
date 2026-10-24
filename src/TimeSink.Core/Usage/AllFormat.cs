using System.Globalization;

namespace TimeSink.Core.Usage;

/// <summary>Number text for the All table cells (invariant digits; the units come from column headers).</summary>
public static class AllFormat
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Hours: one decimal under ten hours ("1.5"), whole hours from ten up ("27").</summary>
    public static string Hours(long seconds)
    {
        var h = Math.Max(0, seconds) / 3600.0;
        if (h < 9.95) return h == 0 ? "0" : h.ToString("0.0", Inv);
        return Math.Round(h, MidpointRounding.AwayFromZero).ToString("0", Inv);
    }

    /// <summary>Average hours per day used, one decimal ("2.6").</summary>
    public static string AveragePerDay(double secondsPerDay) => (Math.Max(0, secondsPerDay) / 3600.0).ToString("0.0", Inv);

    /// <summary>Share of the tracked total as a whole percent; a non-zero share under 1% reads "&lt;1%".</summary>
    public static string Share(double fraction)
    {
        if (fraction <= 0) return "0%";
        var p = (int)Math.Round(fraction * 100, MidpointRounding.AwayFromZero);
        return p == 0 ? "<1%" : p.ToString(Inv) + "%";
    }
}
