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

    public static readonly DependencyProperty HighlightIndexProperty = Reg<int>(nameof(HighlightIndex), -1);

    /// <summary>The session segment drawn emphasised (the rest dim), or -1.</summary>
    public int HighlightIndex { get => (int)GetValue(HighlightIndexProperty); set => SetValue(HighlightIndexProperty, value); }

    /// <summary>The pointer is over a session segment (-1 when it left or is between them); the second value is its position in this control.</summary>
    public event Action<int, Point>? SegmentHovered;

    /// <summary>A session segment was clicked.</summary>
    public event Action<int>? SegmentClicked;

    private const double HitSlop = 4; // the strip is 4 px tall: accept a few pixels around each segment

    private int SegmentAt(Point p)
    {
        if (Segments is null || ActualWidth <= 0) return -1;
        var best = -1;
        var bestGap = double.MaxValue;
        for (var i = 0; i < Segments.Count; i++)
        {
            var x0 = Math.Clamp(Segments[i].Start, 0, 1) * ActualWidth;
            var x1 = Math.Max(x0 + ActualHeight, Math.Clamp(Segments[i].End, 0, 1) * ActualWidth);
            var gap = p.X < x0 ? x0 - p.X : p.X > x1 ? p.X - x1 : 0;
            if (gap <= HitSlop && gap < bestGap) { best = i; bestGap = gap; }
        }
        return best;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        SegmentHovered?.Invoke(SegmentAt(p), p);
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        SegmentHovered?.Invoke(-1, default);
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        var i = SegmentAt(e.GetPosition(this));
        if (i < 0) return; // a click off the segments falls through to the row, which toggles it
        base.OnMouseLeftButtonUp(e);
        SegmentClicked?.Invoke(i);
        e.Handled = true;
    }

    public IReadOnlyList<(double Start, double End)>? Segments { get => (IReadOnlyList<(double, double)>?)GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    public Brush? SegmentBrush { get => (Brush?)GetValue(SegmentBrushProperty); set => SetValue(SegmentBrushProperty, value); }
    public Brush? TrackBrush { get => (Brush?)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, 4);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new Rect(0, -HitSlop, w, h + 2 * HitSlop));
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, 0, w, h), h / 2, h / 2);
        if (Segments is null) return;
        for (var i = 0; i < Segments.Count; i++)
        {
            var (s, e) = Segments[i];
            var x0 = Math.Clamp(s, 0, 1) * w;
            var x1 = Math.Clamp(e, 0, 1) * w;
            var dim = HighlightIndex >= 0 && i != HighlightIndex;
            if (dim) dc.PushOpacity(0.35);
            dc.DrawRoundedRectangle(SegmentBrush, null, new Rect(x0, 0, Math.Max(h, x1 - x0), h), h / 2, h / 2);
            if (dim) dc.Pop();
        }
    }
}
