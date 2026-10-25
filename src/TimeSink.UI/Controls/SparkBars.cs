using System.Windows;
using System.Windows.Media;

namespace TimeSink.UI.Controls;

/// <summary>A tiny bar sparkline (one bar per value, scaled to the largest). Zero days keep a 1 px stub.</summary>
public sealed class SparkBars : FrameworkElement
{
    private const double Gap = 2;

    private static DependencyProperty Reg<T>(string name, T def = default!) =>
        DependencyProperty.Register(name, typeof(T), typeof(SparkBars),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ValuesProperty = Reg<IReadOnlyList<long>?>(nameof(Values));
    public static readonly DependencyProperty BarBrushProperty = Reg<Brush?>(nameof(BarBrush));

    public IReadOnlyList<long>? Values { get => (IReadOnlyList<long>?)GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public Brush? BarBrush { get => (Brush?)GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(56, 22);

    /// <summary>The pointer is over a bar (-1 when it left); the second value is its position in this control.</summary>
    public event Action<int, Point>? BarHovered;

    /// <summary>A bar was clicked.</summary>
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
        if (i < 0) return;
        BarClicked?.Invoke(i);
        e.Handled = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        ChartSupport.HitArea(dc, w, h);
        if (Values is not { Count: > 0 } v) return;
        var max = Math.Max(1, v.Max());
        var barW = (w - Gap * (v.Count - 1)) / v.Count;
        for (var i = 0; i < v.Count; i++)
        {
            var bh = Math.Max(1, v[i] / (double)max * h);
            dc.DrawRoundedRectangle(BarBrush, null, new Rect(i * (barW + Gap), h - bh, barW, bh), 1, 1);
        }
    }
}
