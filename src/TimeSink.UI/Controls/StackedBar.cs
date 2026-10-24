using System.Windows;
using System.Windows.Media;

namespace TimeSink.UI.Controls;

/// <summary>A thin horizontal bar split into proportional rounded segments (e.g. one day's time by app).</summary>
public sealed class StackedBar : FrameworkElement
{
    private const double Gap = 2;

    private static DependencyProperty Reg<T>(string name, T def = default!) =>
        DependencyProperty.Register(name, typeof(T), typeof(StackedBar),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SegmentsProperty = Reg<IReadOnlyList<(double Weight, Brush Brush)>?>(nameof(Segments));
    public static readonly DependencyProperty TrackBrushProperty = Reg<Brush?>(nameof(TrackBrush));

    public IReadOnlyList<(double Weight, Brush Brush)>? Segments { get => (IReadOnlyList<(double, Brush)>?)GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    public Brush? TrackBrush { get => (Brush?)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, 6);

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, 0, w, h), h / 2, h / 2);
        if (Segments is not { Count: > 0 } segs) return;
        var sum = segs.Sum(s => Math.Max(0, s.Weight));
        if (sum <= 0) return;
        var usable = Math.Max(0, w - Gap * (segs.Count - 1));
        var x = 0.0;
        foreach (var (weight, brush) in segs)
        {
            var sw = Math.Max(0, weight) / sum * usable;
            if (sw >= 0.5) dc.DrawRoundedRectangle(brush, null, new Rect(x, 0, sw, h), 1.5, 1.5);
            x += sw + Gap;
        }
    }
}
