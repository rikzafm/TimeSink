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

    private const double BarHeight = 6, HitHeight = 16;

    private int _hover = -1, _pinned = -1;

    /// <summary>Segment under the pointer, or -1 when the pointer leaves the bar.</summary>
    public event Action<int>? HoverChanged;

    /// <summary>Segment clicked; clicking the pinned segment again reports -1.</summary>
    public event Action<int>? SegmentClicked;

    public int PinnedIndex => _pinned;

    public StackedBar() => Cursor = System.Windows.Input.Cursors.Hand;

    public void ClearPinned() { _pinned = -1; _hover = -1; InvalidateVisual(); }

    protected override Size MeasureOverride(Size availableSize) => new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, HitHeight);

    private IEnumerable<(int Index, double X, double Width)> Layout()
    {
        if (Segments is not { Count: > 0 } segs) yield break;
        var sum = segs.Sum(s => Math.Max(0, s.Weight));
        if (sum <= 0) yield break;
        var usable = Math.Max(0, ActualWidth - Gap * (segs.Count - 1));
        var x = 0.0;
        for (var i = 0; i < segs.Count; i++)
        {
            var sw = Math.Max(0, segs[i].Weight) / sum * usable;
            yield return (i, x, sw);
            x += sw + Gap;
        }
    }

    private int HitTest(System.Windows.Input.MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        foreach (var (i, x, w) in Layout())
            if (p.X >= x - Gap / 2 && p.X <= x + w + Gap / 2) return i;
        return -1;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        var i = HitTest(e);
        if (i == _hover) return;
        _hover = i;
        InvalidateVisual();
        HoverChanged?.Invoke(i);
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        if (_hover < 0) return;
        _hover = -1;
        InvalidateVisual();
        HoverChanged?.Invoke(-1);
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        var i = HitTest(e);
        if (i < 0) return;
        _pinned = i == _pinned ? -1 : i;
        InvalidateVisual();
        SegmentClicked?.Invoke(_pinned);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = BarHeight, y = (ActualHeight - h) / 2;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, ActualHeight)); // hit area
        dc.DrawRoundedRectangle(TrackBrush, null, new Rect(0, y, w, h), h / 2, h / 2);
        if (Segments is not { Count: > 0 } segs) return;
        var focus = _hover >= 0 ? _hover : _pinned;
        foreach (var (i, x, sw) in Layout())
        {
            if (sw < 0.5) continue;
            var on = focus < 0 || i == focus;
            dc.PushOpacity(on ? 1 : 0.35);
            var grow = i == focus ? 1.5 : 0;
            dc.DrawRoundedRectangle(segs[i].Brush, null, new Rect(x, y - grow, sw, h + grow * 2), 1.5, 1.5);
            dc.Pop();
        }
    }
}
