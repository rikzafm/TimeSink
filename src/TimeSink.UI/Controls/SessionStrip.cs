using System.Windows;
using System.Windows.Media;

namespace TimeSink.UI.Controls;

/// <summary>A 24-hour mini timeline: a track with one rounded segment per session (fractions of the day).</summary>
public sealed class SessionStrip : FrameworkElement
{
    private static DependencyProperty Reg<T>(string name, T def = default!) =>
        DependencyProperty.Register(name, typeof(T), typeof(SessionStrip),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SegmentsProperty = Reg<IReadOnlyList<(double Start, double End)>?>(nameof(Segments));
    public static readonly DependencyProperty SegmentBrushProperty = Reg<Brush?>(nameof(SegmentBrush));
    public static readonly DependencyProperty TrackBrushProperty = Reg<Brush?>(nameof(TrackBrush));

    public IReadOnlyList<(double Start, double End)>? Segments { get => (IReadOnlyList<(double, double)>?)GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    public Brush? SegmentBrush { get => (Brush?)GetValue(SegmentBrushProperty); set => SetValue(SegmentBrushProperty, value); }
    public Brush? TrackBrush { get => (Brush?)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, 4);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, 0, w, h), h / 2, h / 2);
        if (Segments is null) return;
        foreach (var (s, e) in Segments)
        {
            var x0 = Math.Clamp(s, 0, 1) * w;
            var x1 = Math.Clamp(e, 0, 1) * w;
            dc.DrawRoundedRectangle(SegmentBrush, null, new Rect(x0, 0, Math.Max(h, x1 - x0), h), h / 2, h / 2);
        }
    }
}
