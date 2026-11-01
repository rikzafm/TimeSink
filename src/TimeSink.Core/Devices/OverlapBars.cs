using System;

namespace TimeSink.Core.Devices;

public readonly record struct OverlapBarGeometry(
    double X,
    double Width,
    double Height,
    bool IsDeviceA,
    bool InFront
);

/// <summary>
/// Geometry helper for drawing two bars side by side that overlap within a column:
/// each bar is 66% of column width (overlap 34%), the shorter bar is placed in front,
/// and a single device's bar takes the full column width.
/// </summary>
public static class OverlapBars
{
    public const double OverlapWidthRatio = 0.66;

    /// <summary>
    /// Computes the layout of two overlapping bars within a column of width <paramref name="columnWidth"/> at <paramref name="columnX"/>.
    /// Returns the bars in draw order: back first, front second.
    /// </summary>
    public static (OverlapBarGeometry Back, OverlapBarGeometry Front) Layout(
        double columnX, double columnWidth, double heightA, double heightB)
    {
        heightA = Math.Max(0, heightA);
        heightB = Math.Max(0, heightB);

        if (heightA > 0 && heightB > 0)
        {
            var barW = columnWidth * OverlapWidthRatio;
            var xA = columnX;
            var xB = columnX + columnWidth - barW;

            // Shorter bar in front (drawn second)
            if (heightA <= heightB)
            {
                var back = new OverlapBarGeometry(xB, barW, heightB, IsDeviceA: false, InFront: false);
                var front = new OverlapBarGeometry(xA, barW, heightA, IsDeviceA: true, InFront: true);
                return (back, front);
            }
            else
            {
                var back = new OverlapBarGeometry(xA, barW, heightA, IsDeviceA: true, InFront: false);
                var front = new OverlapBarGeometry(xB, barW, heightB, IsDeviceA: false, InFront: true);
                return (back, front);
            }
        }

        if (heightA > 0)
        {
            var solo = new OverlapBarGeometry(columnX, columnWidth, heightA, IsDeviceA: true, InFront: true);
            var empty = new OverlapBarGeometry(columnX, 0, 0, IsDeviceA: false, InFront: false);
            return (empty, solo);
        }

        if (heightB > 0)
        {
            var solo = new OverlapBarGeometry(columnX, columnWidth, heightB, IsDeviceA: false, InFront: true);
            var empty = new OverlapBarGeometry(columnX, 0, 0, IsDeviceA: true, InFront: false);
            return (empty, solo);
        }

        var zeroA = new OverlapBarGeometry(columnX, 0, 0, IsDeviceA: true, InFront: false);
        var zeroB = new OverlapBarGeometry(columnX, 0, 0, IsDeviceA: false, InFront: false);
        return (zeroA, zeroB);
    }
}

