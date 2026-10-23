using System.Globalization;

namespace TimeSink.Core.Flyout;

/// <summary>Anchor geometry the Agent hands to the UI process on its command line.</summary>
public readonly record struct FlyoutLaunchArgs(PxRect Anchor, PxRect Monitor, PxRect Work, int Dpi)
{
    public string[] ToArgs() =>
    [
        "--anchor", Format(Anchor),
        "--monitor", Format(Monitor),
        "--work", Format(Work),
        "--dpi", Dpi.ToString(CultureInfo.InvariantCulture),
    ];

    public static bool TryParse(string[] args, out FlyoutLaunchArgs result)
    {
        result = default;
        if (!TryGet(args, "--anchor", out var a) || !TryRect(a, out var anchor)) return false;
        if (!TryGet(args, "--monitor", out var m) || !TryRect(m, out var monitor)) return false;
        if (!TryGet(args, "--work", out var w) || !TryRect(w, out var work)) return false;
        if (!TryGet(args, "--dpi", out var d) ||
            !int.TryParse(d, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dpi) || dpi <= 0) return false;
        result = new FlyoutLaunchArgs(anchor, monitor, work, dpi);
        return true;
    }

    private static string Format(PxRect r) => string.Create(CultureInfo.InvariantCulture,
        $"{r.Left},{r.Top},{r.Right},{r.Bottom}");

    private static bool TryGet(string[] args, string name, out string value)
    {
        for (var i = 0; i < args.Length - 1; i++)
            if (args[i] == name) { value = args[i + 1]; return true; }
        value = "";
        return false;
    }

    private static bool TryRect(string s, out PxRect rect)
    {
        rect = default;
        var p = s.Split(',');
        if (p.Length != 4) return false;
        Span<int> v = stackalloc int[4];
        for (var i = 0; i < 4; i++)
            if (!int.TryParse(p[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out v[i])) return false;
        rect = new PxRect(v[0], v[1], v[2], v[3]);
        return true;
    }
}
