using System.Windows;
using System.Windows.Media;
using TimeSink.Core.Usage;

namespace TimeSink.UI.Controls;

/// <summary>
/// The Week tab's seven columns: height proportional to the day's time (scaled to the busiest day), the current day in
/// the accent colour, days still to come as a short empty stub. Plain primitives; colours are DPs bound to theme brushes.
/// </summary>
public sealed class WeekColumns : FrameworkElement
{
    private const double Gap = 6;

    private static DependencyProperty Reg<T>(string name, T def = default!) =>
        DependencyProperty.Register(name, typeof(T), typeof(WeekColumns),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DaysProperty = Reg<IReadOnlyList<WeekDay>?>(nameof(Days));
    public static readonly DependencyProperty ColumnBrushProperty = Reg<Brush?>(nameof(ColumnBrush));
    public static readonly DependencyProperty CurrentBrushProperty = Reg<Brush?>(nameof(CurrentBrush));
    public static readonly DependencyProperty FutureBrushProperty = Reg<Brush?>(nameof(FutureBrush));

    public static readonly DependencyProperty SelectedBrushProperty = Reg<Brush?>(nameof(SelectedBrush));

    /// <summary>The outline colour of the selected column.</summary>
    public Brush? SelectedBrush { get => (Brush?)GetValue(SelectedBrushProperty); set => SetValue(SelectedBrushProperty, value); }

    public static readonly DependencyProperty SelectedIndexProperty = Reg<int>(nameof(SelectedIndex), -1);

    /// <summary>The column outlined as selected, or -1.</summary>
    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }

    /// <summary>The pointer is over a column (-1 when it left); the second value is its position in this control.</summary>
    public event Action<int, Point>? ColumnHovered;

    /// <summary>A column was clicked (future days are not clickable).</summary>
    public event Action<int>? ColumnClicked;

    private int IndexAt(Point p)
    {
        if (Days is not { Count: > 0 } days || ActualWidth <= 0 || p.X < 0 || p.X >= ActualWidth) return -1;
        var i = Math.Clamp((int)(p.X / ActualWidth * days.Count), 0, days.Count - 1);
        return days[i].IsFuture ? -1 : i;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        ColumnHovered?.Invoke(IndexAt(p), p);
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        ColumnHovered?.Invoke(-1, default);
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var i = IndexAt(e.GetPosition(this));
        if (i >= 0) ColumnClicked?.Invoke(i);
    }

    public IReadOnlyList<WeekDay>? Days { get => (IReadOnlyList<WeekDay>?)GetValue(DaysProperty); set => SetValue(DaysProperty, value); }
    public Brush? ColumnBrush { get => (Brush?)GetValue(ColumnBrushProperty); set => SetValue(ColumnBrushProperty, value); }
    public Brush? CurrentBrush { get => (Brush?)GetValue(CurrentBrushProperty); set => SetValue(CurrentBrushProperty, value); }
    public Brush? FutureBrush { get => (Brush?)GetValue(FutureBrushProperty); set => SetValue(FutureBrushProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        ChartSupport.HitArea(dc, w, h);
        if (Days is not { Count: > 0 } days || w <= 0) return;
        var max = Math.Max(1, days.Max(d => d.Seconds));
        var colW = (w - Gap * (days.Count - 1)) / days.Count;
        for (var i = 0; i < days.Count; i++)
        {
            var d = days[i];
            var x = i * (colW + Gap);
            var barH = d.IsFuture ? 6 : Math.Max(6, d.Seconds / (double)max * h);
            var brush = d.IsFuture ? FutureBrush : d.IsToday ? CurrentBrush : ColumnBrush;
            dc.DrawRoundedRectangle(brush, null, new Rect(x, h - barH, colW, barH), 3, 3);
            if (i == SelectedIndex && SelectedBrush is { } sel)
                dc.DrawRoundedRectangle(null, new Pen(sel, 1.5), new Rect(x - 1.5, h - barH - 1.5, colW + 3, barH + 3), 4, 4);
        }
    }
}
