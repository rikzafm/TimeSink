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

    protected override void OnRender(DrawingContext dc)
    {
        if (Values is not { Count: > 0 } v) return;
        var w = ActualWidth;
        var h = ActualHeight;
        var max = Math.Max(1, v.Max());
        var barW = (w - Gap * (v.Count - 1)) / v.Count;
        for (var i = 0; i < v.Count; i++)
        {
            var bh = Math.Max(1, v[i] / (double)max * h);
            dc.DrawRoundedRectangle(BarBrush, null, new Rect(i * (barW + Gap), h - bh, barW, bh), 1, 1);
        }
    }
}
