using System.Windows;
using System.Windows.Media;

namespace TimeSink.UI.Controls;

/// <summary>The Week tab's trend strip: one slim bar per week (scaled to the busiest), the last one in the accent colour.</summary>
public sealed class WeekTrend : FrameworkElement
{
    private const double Gap = 3;

    private static DependencyProperty Reg<T>(string name, T def = default!) =>
        DependencyProperty.Register(name, typeof(T), typeof(WeekTrend),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValuesProperty = Reg<IReadOnlyList<long>?>(nameof(Values));
    public static readonly DependencyProperty BarBrushProperty = Reg<Brush?>(nameof(BarBrush));
    public static readonly DependencyProperty CurrentBrushProperty = Reg<Brush?>(nameof(CurrentBrush));

    public IReadOnlyList<long>? Values { get => (IReadOnlyList<long>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public Brush? BarBrush { get => (Brush?)GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
    public Brush? CurrentBrush { get => (Brush?)GetValue(CurrentBrushProperty); set => SetValue(CurrentBrushProperty, value); }

    /// <summary>The pointer is over a week bar (-1 when it left); the second value is its position in this control.</summary>
    public event Action<int, Point>? BarHovered;

    /// <summary>A week bar was clicked.</summary>
    public event Action<int>? BarClicked;

    private int IndexAt(Point p)
    {
        if (Values is not { Count: > 0 } v || ActualWidth <= 0 || p.X < 0 || p.X >= ActualWidth) return -1;
        return Math.Clamp((int)(p.X / ActualWidth * v.Count), 0, v.Count - 1);
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        BarHovered?.Invoke(IndexAt(p), p);
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        BarHovered?.Invoke(-1, default);
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var i = IndexAt(e.GetPosition(this));
        if (i >= 0) BarClicked?.Invoke(i);
    }

    protected override void OnRender(DrawingContext dc)
    {
        ChartSupport.HitArea(dc, ActualWidth, ActualHeight);
        if (Values is not { Count: > 0 } v || ActualWidth <= 0) return;
        var max = Math.Max(1, v.Max());
        var barW = (ActualWidth - Gap * (v.Count - 1)) / v.Count;
        for (var i = 0; i < v.Count; i++)
        {
            var bh = Math.Max(3, v[i] / (double)max * ActualHeight);
            dc.DrawRoundedRectangle(i == v.Count - 1 ? CurrentBrush : BarBrush, null, new Rect(i * (barW + Gap), ActualHeight - bh, barW, bh), 2, 2);
        }
    }
}
