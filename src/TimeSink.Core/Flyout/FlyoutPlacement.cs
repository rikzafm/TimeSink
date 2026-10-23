namespace TimeSink.Core.Flyout;

/// <summary>Rectangle in physical pixels (virtual-screen coordinates); Right/Bottom exclusive.</summary>
public readonly record struct PxRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public int CenterX => Left + Width / 2;
    public int CenterY => Top + Height / 2;
}

public readonly record struct PxSize(int Width, int Height);

public static class FlyoutPlacement
{
    /// <summary>
    /// Positions the flyout flush against the taskbar edge (inferred from monitor vs work area),
    /// aligned to the tray icon, and clamped inside the work area.
    /// </summary>
    public static PxRect Compute(PxRect icon, PxRect monitor, PxRect work, PxSize size)
    {
        var w = Math.Min(size.Width, work.Width);
        var h = Math.Min(size.Height, work.Height);

        int x, y;
        switch (DetectEdge(icon, monitor, work))
        {
            case Edge.Top:
                y = work.Top;
                x = icon.CenterX - w / 2;
                break;
            case Edge.Left:
                x = work.Left;
                y = icon.CenterY - h / 2;
                break;
            case Edge.Right:
                x = work.Right - w;
                y = icon.CenterY - h / 2;
                break;
            default:
                y = work.Bottom - h;
                x = icon.CenterX - w / 2;
                break;
        }

        x = Math.Clamp(x, work.Left, work.Right - w);
        y = Math.Clamp(y, work.Top, work.Bottom - h);
        return new PxRect(x, y, x + w, y + h);
    }

    private enum Edge { Bottom, Top, Left, Right }

    private static Edge DetectEdge(PxRect icon, PxRect monitor, PxRect work)
    {
        if (work.Bottom < monitor.Bottom) return Edge.Bottom;
        if (work.Top > monitor.Top) return Edge.Top;
        if (work.Left > monitor.Left) return Edge.Left;
        if (work.Right < monitor.Right) return Edge.Right;

        // Auto-hide taskbar: work area equals the monitor. Use the monitor edge nearest the icon.
        var toTop = Math.Abs(icon.Top - monitor.Top);
        var toBottom = Math.Abs(monitor.Bottom - icon.Bottom);
        var toLeft = Math.Abs(icon.Left - monitor.Left);
        var toRight = Math.Abs(monitor.Right - icon.Right);
        var min = Math.Min(Math.Min(toTop, toBottom), Math.Min(toLeft, toRight));
        if (min == toBottom) return Edge.Bottom;
        if (min == toTop) return Edge.Top;
        return min == toLeft ? Edge.Left : Edge.Right;
    }
}
