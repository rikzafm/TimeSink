using System.Windows;
using System.Windows.Media;

namespace TimeSink.UI.Controls;

/// <summary>
/// A 24-hour dial for one app: the outer ring has one arc per session (clockwise from midnight at the top), the inner
/// ring fills with the app's time as a share of the day and a tick marks its 7-day average. Square; the caller sets Width.
/// </summary>
public sealed class SessionDial : FrameworkElement
{
    private const double Design = 260;       // geometry below is authored on a 260 unit board and scaled
    private const double OuterRadius = 100, OuterStroke = 14, InnerRadius = 76, InnerStroke = 14, TickInner = 66, TickOuter = 88;
    private const double OuterRadiusMulti = 104, OuterStrokeMulti = 11;
    private const double MiddleRadiusMulti = 86, MiddleStrokeMulti = 11;
    private const double InnerRadiusMulti = 68, InnerStrokeMulti = 11;
    private const double TickInnerMulti = 58, TickOuterMulti = 80;
    private const double TotalOpacity = 0.5; // the inner fill is the app colour, softer than the session arcs

    private static DependencyProperty Reg<T>(string name, T def = default!) =>
        DependencyProperty.Register(name, typeof(T), typeof(SessionDial),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SegmentsProperty = Reg<IReadOnlyList<(double Start, double End)>?>(nameof(Segments));
    public static readonly DependencyProperty TailsProperty = Reg<IReadOnlyList<double?>?>(nameof(Tails));
    public static readonly DependencyProperty NowFractionProperty = Reg<double?>(nameof(NowFraction));
    public static readonly DependencyProperty TotalFractionProperty = Reg<double>(nameof(TotalFraction));
    public static readonly DependencyProperty AverageFractionProperty = Reg<double?>(nameof(AverageFraction));
    public static readonly DependencyProperty CenterTextProperty = Reg<string?>(nameof(CenterText));
    public static readonly DependencyProperty SegmentBrushProperty = Reg<Brush?>(nameof(SegmentBrush));
    public static readonly DependencyProperty TrackBrushProperty = Reg<Brush?>(nameof(TrackBrush));
    public static readonly DependencyProperty TickBrushProperty = Reg<Brush?>(nameof(TickBrush));
    public static readonly DependencyProperty NowBrushProperty = Reg<Brush?>(nameof(NowBrush));
    public static readonly DependencyProperty CenterBrushProperty = Reg<Brush?>(nameof(CenterBrush));
    public static readonly DependencyProperty LabelBrushProperty = Reg<Brush?>(nameof(LabelBrush));
    public static readonly DependencyProperty NumberFontProperty = Reg<FontFamily?>(nameof(NumberFont));

    public static readonly DependencyProperty IsMultiDeviceProperty = Reg<bool>(nameof(IsMultiDevice), false);
    public static readonly DependencyProperty SegmentsAProperty = Reg<IReadOnlyList<(double Start, double End, int Index)>?>(nameof(SegmentsA));
    public static readonly DependencyProperty TailsAProperty = Reg<IReadOnlyList<double?>?>(nameof(TailsA));
    public static readonly DependencyProperty SegmentsBProperty = Reg<IReadOnlyList<(double Start, double End, int Index)>?>(nameof(SegmentsB));
    public static readonly DependencyProperty TailsBProperty = Reg<IReadOnlyList<double?>?>(nameof(TailsB));
    public static readonly DependencyProperty DeviceABrushProperty = Reg<Brush?>(nameof(DeviceABrush));
    public static readonly DependencyProperty DeviceBBrushProperty = Reg<Brush?>(nameof(DeviceBBrush));
    public static readonly DependencyProperty CenterSubTextProperty = Reg<string?>(nameof(CenterSubText));

    public bool IsMultiDevice { get => (bool)GetValue(IsMultiDeviceProperty); set => SetValue(IsMultiDeviceProperty, value); }
    public IReadOnlyList<(double Start, double End, int Index)>? SegmentsA { get => (IReadOnlyList<(double, double, int)>?)GetValue(SegmentsAProperty); set => SetValue(SegmentsAProperty, value); }
    public IReadOnlyList<double?>? TailsA { get => (IReadOnlyList<double?>?)GetValue(TailsAProperty); set => SetValue(TailsAProperty, value); }
    public IReadOnlyList<(double Start, double End, int Index)>? SegmentsB { get => (IReadOnlyList<(double, double, int)>?)GetValue(SegmentsBProperty); set => SetValue(SegmentsBProperty, value); }
    public IReadOnlyList<double?>? TailsB { get => (IReadOnlyList<double?>?)GetValue(TailsBProperty); set => SetValue(TailsBProperty, value); }
    public Brush? DeviceABrush { get => (Brush?)GetValue(DeviceABrushProperty); set => SetValue(DeviceABrushProperty, value); }
    public Brush? DeviceBBrush { get => (Brush?)GetValue(DeviceBBrushProperty); set => SetValue(DeviceBBrushProperty, value); }
    public string? CenterSubText { get => (string?)GetValue(CenterSubTextProperty); set => SetValue(CenterSubTextProperty, value); }

    public IReadOnlyList<(double Start, double End)>? Segments { get => (IReadOnlyList<(double, double)>?)GetValue(SegmentsProperty); set => SetValue(SegmentsProperty, value); }
    /// <summary>
    /// Per segment, where its part before midnight began (0 to 1, drawn dotted from there round to the top); null for a
    /// session that started within the day. Same length as <see cref="Segments"/>.
    /// </summary>
    public IReadOnlyList<double?>? Tails { get => (IReadOnlyList<double?>?)GetValue(TailsProperty); set => SetValue(TailsProperty, value); }
    /// <summary>Wall-clock position of now (0 to 1); null on a past day.</summary>
    public double? NowFraction { get => (double?)GetValue(NowFractionProperty); set => SetValue(NowFractionProperty, value); }
    /// <summary>App time as a share of the day (0 to 1).</summary>
    public double TotalFraction { get => (double)GetValue(TotalFractionProperty); set => SetValue(TotalFractionProperty, value); }
    /// <summary>7-day average as a share of the day; null draws no tick.</summary>
    public double? AverageFraction { get => (double?)GetValue(AverageFractionProperty); set => SetValue(AverageFractionProperty, value); }
    public string? CenterText { get => (string?)GetValue(CenterTextProperty); set => SetValue(CenterTextProperty, value); }
    public Brush? SegmentBrush { get => (Brush?)GetValue(SegmentBrushProperty); set => SetValue(SegmentBrushProperty, value); }
    public Brush? TrackBrush { get => (Brush?)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush? TickBrush { get => (Brush?)GetValue(TickBrushProperty); set => SetValue(TickBrushProperty, value); }
    public Brush? NowBrush { get => (Brush?)GetValue(NowBrushProperty); set => SetValue(NowBrushProperty, value); }
    public Brush? CenterBrush { get => (Brush?)GetValue(CenterBrushProperty); set => SetValue(CenterBrushProperty, value); }
    public Brush? LabelBrush { get => (Brush?)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public FontFamily? NumberFont { get => (FontFamily?)GetValue(NumberFontProperty); set => SetValue(NumberFontProperty, value); }

    public static readonly DependencyProperty HighlightIndexProperty = Reg<int>(nameof(HighlightIndex), -1);

    /// <summary>The session arc drawn emphasised (the rest dim), or -1.</summary>
    public int HighlightIndex { get => (int)GetValue(HighlightIndexProperty); set => SetValue(HighlightIndexProperty, value); }

    /// <summary>The pointer is over a session arc (-1 when it left or is between arcs).</summary>
    public event Action<int>? SegmentHovered;

    /// <summary>A session arc was clicked.</summary>
    public event Action<int>? SegmentClicked;

    private int SegmentAt(Point p)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return -1;
        var k = size / Design;
        double dx = p.X - size / 2, dy = p.Y - size / 2;
        var r = Math.Sqrt(dx * dx + dy * dy);
        var f = Math.Atan2(dx, -dy) / (2 * Math.PI);
        if (f < 0) f += 1;
        var slop = MinSweep(k);

        if (IsMultiDevice)
        {
            if (SegmentsA is not null && Math.Abs(r - OuterRadiusMulti * k) <= (OuterStrokeMulti / 2 + 4) * k)
            {
                for (var i = 0; i < SegmentsA.Count; i++)
                {
                    if (f >= SegmentsA[i].Start - slop && f <= Math.Max(SegmentsA[i].End, SegmentsA[i].Start + 2 * slop) + slop) return SegmentsA[i].Index;
                    if (TailsA is { } ta && i < ta.Count && ta[i] is { } tail && f >= tail) return SegmentsA[i].Index;
                }
            }
            if (SegmentsB is not null && Math.Abs(r - MiddleRadiusMulti * k) <= (MiddleStrokeMulti / 2 + 4) * k)
            {
                for (var i = 0; i < SegmentsB.Count; i++)
                {
                    if (f >= SegmentsB[i].Start - slop && f <= Math.Max(SegmentsB[i].End, SegmentsB[i].Start + 2 * slop) + slop) return SegmentsB[i].Index;
                    if (TailsB is { } tb && i < tb.Count && tb[i] is { } tail && f >= tail) return SegmentsB[i].Index;
                }
            }
            return -1;
        }

        if (Segments is null) return -1;
        if (Math.Abs(r - OuterRadius * k) > (OuterStroke / 2 + 4) * k) return -1;
        for (var i = 0; i < Segments.Count; i++)
        {
            if (f >= Segments[i].Start - slop && f <= Math.Max(Segments[i].End, Segments[i].Start + 2 * slop) + slop) return i;
            if (TailOf(i) is { } tail && f >= tail) return i;
        }
        return -1;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        SegmentHovered?.Invoke(SegmentAt(e.GetPosition(this)));
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        SegmentHovered?.Invoke(-1);
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        var i = SegmentAt(e.GetPosition(this));
        if (i < 0) return;
        base.OnMouseLeftButtonUp(e);
        SegmentClicked?.Invoke(i);
        e.Handled = true; // the row card around the dial would otherwise collapse
    }

    /// <summary>The caller sets Width and Height (square); without them the dial takes no room.</summary>
    protected override Size MeasureOverride(Size availableSize) => new(double.IsNaN(Width) ? 0 : Width, double.IsNaN(Height) ? 0 : Height);

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var k = size / Design;
        var c = new Point(size / 2, size / 2);

        Pen Ring(Brush? b, double stroke) { var p = new Pen(b, stroke * k) { StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat }; p.Freeze(); return p; }

        if (IsMultiDevice)
        {
            dc.DrawEllipse(Brushes.Transparent, null, c, size / 2, size / 2);
            dc.DrawEllipse(null, Ring(TrackBrush, OuterStrokeMulti), c, OuterRadiusMulti * k, OuterRadiusMulti * k);
            dc.DrawEllipse(null, Ring(TrackBrush, MiddleStrokeMulti), c, MiddleRadiusMulti * k, MiddleRadiusMulti * k);
            dc.DrawEllipse(null, Ring(TrackBrush, InnerStrokeMulti), c, InnerRadiusMulti * k, InnerRadiusMulti * k);

            var outerA = Ring(DeviceABrush ?? SegmentBrush, OuterStrokeMulti);
            var dottedA = new Pen(DeviceABrush ?? SegmentBrush, OuterStrokeMulti * k)
            {
                DashStyle = new DashStyle(new DoubleCollection { 0.17, 0.5 }, 0),
                DashCap = PenLineCap.Flat, StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat,
            };
            dottedA.Freeze();
            var carriedA = false;
            if (SegmentsA is not null)
                for (var i = 0; i < SegmentsA.Count; i++)
                {
                    var (s, e, idx) = SegmentsA[i];
                    var dim = HighlightIndex >= 0 && idx != HighlightIndex;
                    if (dim) dc.PushOpacity(0.35);
                    if (TailsA is { } ta && i < ta.Count && ta[i] is { } tail)
                    {
                        carriedA = true;
                        dc.PushOpacity(0.75);
                        Arc(dc, dottedA, c, OuterRadiusMulti * k, tail, 1);
                        dc.Pop();
                    }
                    Arc(dc, outerA, c, OuterRadiusMulti * k, s, Math.Max(e, s + MinSweep(k)));
                    if (dim) dc.Pop();
                }
            if (carriedA)
            {
                var tick = new Pen(TickBrush, Math.Max(1.5, 1.5 * k * 1.6));
                dc.DrawLine(tick, At(c, (OuterRadiusMulti - OuterStrokeMulti / 2 - 4) * k, 0), At(c, (OuterRadiusMulti + OuterStrokeMulti / 2 + 1) * k, 0));
            }

            var middleB = Ring(DeviceBBrush ?? SegmentBrush, MiddleStrokeMulti);
            var dottedB = new Pen(DeviceBBrush ?? SegmentBrush, MiddleStrokeMulti * k)
            {
                DashStyle = new DashStyle(new DoubleCollection { 0.17, 0.5 }, 0),
                DashCap = PenLineCap.Flat, StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat,
            };
            dottedB.Freeze();
            var carriedB = false;
            if (SegmentsB is not null)
                for (var i = 0; i < SegmentsB.Count; i++)
                {
                    var (s, e, idx) = SegmentsB[i];
                    var dim = HighlightIndex >= 0 && idx != HighlightIndex;
                    if (dim) dc.PushOpacity(0.35);
                    if (TailsB is { } tb && i < tb.Count && tb[i] is { } tail)
                    {
                        carriedB = true;
                        dc.PushOpacity(0.75);
                        Arc(dc, dottedB, c, MiddleRadiusMulti * k, tail, 1);
                        dc.Pop();
                    }
                    Arc(dc, middleB, c, MiddleRadiusMulti * k, s, Math.Max(e, s + MinSweep(k)));
                    if (dim) dc.Pop();
                }
            if (carriedB)
            {
                var tick = new Pen(TickBrush, Math.Max(1.5, 1.5 * k * 1.6));
                dc.DrawLine(tick, At(c, (MiddleRadiusMulti - MiddleStrokeMulti / 2 - 4) * k, 0), At(c, (MiddleRadiusMulti + MiddleStrokeMulti / 2 + 1) * k, 0));
            }

            var totalM = Math.Clamp(TotalFraction, 0, 1);
            if (totalM > 0)
            {
                dc.PushOpacity(TotalOpacity);
                Arc(dc, Ring(SegmentBrush, InnerStrokeMulti), c, InnerRadiusMulti * k, 0, totalM);
                dc.Pop();
            }

            if (AverageFraction is { } avg && avg > 0)
            {
                var a = Math.Clamp(avg, 0, 1) * 2 * Math.PI;
                var tick = new Pen(TickBrush, Math.Max(2, 3 * k * 1.6)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(tick, At(c, TickInnerMulti * k, a), At(c, TickOuterMulti * k, a));
            }

            if (NowFraction is { } now)
            {
                var a = Math.Clamp(now, 0, 1) * 2 * Math.PI;
                var dot = Math.Max(4, 7 * k);
                dc.DrawEllipse(ChartSupport.Brush(this, "Brush.Panel"), new Pen(NowBrush, Math.Max(2, 4 * k)), At(c, OuterRadiusMulti * k, a), dot, dot);
            }

            var label = LabelBrush;
            var lsize = Math.Max(9, 14 * k);
            foreach (var h in new[] { 0, 6, 12, 18 })
            {
                var t = ChartSupport.Text(this, h.ToString("00"), NumberFont, lsize, label, FontWeights.Normal);
                var a = h / 24.0 * 2 * Math.PI;
                var p = At(c, (OuterRadiusMulti + OuterStrokeMulti / 2 + 9) * k, a);
                dc.DrawText(t, new Point(p.X - t.Width / 2, p.Y - t.Height / 2));
            }

            if (!string.IsNullOrEmpty(CenterText))
            {
                var t = ChartSupport.Text(this, CenterText!, NumberFont, 32 * k, CenterBrush, FontWeights.Bold);
                if (!string.IsNullOrEmpty(CenterSubText))
                {
                    var sub = ChartSupport.Text(this, CenterSubText!, null, 10 * k, LabelBrush, FontWeights.Normal);
                    var totalH = t.Height + sub.Height + 2;
                    var topY = c.Y - totalH / 2;
                    dc.DrawText(t, new Point(c.X - t.Width / 2, topY));
                    dc.DrawText(sub, new Point(c.X - sub.Width / 2, topY + t.Height + 2));
                }
                else
                {
                    dc.DrawText(t, new Point(c.X - t.Width / 2, c.Y - t.Height / 2));
                }
            }
            return;
        }

        dc.DrawEllipse(Brushes.Transparent, null, c, size / 2, size / 2);
        dc.DrawEllipse(null, Ring(TrackBrush, OuterStroke), c, OuterRadius * k, OuterRadius * k);
        dc.DrawEllipse(null, Ring(TrackBrush, InnerStroke), c, InnerRadius * k, InnerRadius * k);

        var outer = Ring(SegmentBrush, OuterStroke);
        var dotted = new Pen(SegmentBrush, OuterStroke * k)
        {
            DashStyle = new DashStyle(new DoubleCollection { 0.17, 0.5 }, 0),
            DashCap = PenLineCap.Flat, StartLineCap = PenLineCap.Flat, EndLineCap = PenLineCap.Flat,
        };
        dotted.Freeze();
        var carried = false;
        if (Segments is not null)
            for (var i = 0; i < Segments.Count; i++)
            {
                var (s, e) = Segments[i];
                var dim = HighlightIndex >= 0 && i != HighlightIndex;
                if (dim) dc.PushOpacity(0.35);
                if (TailOf(i) is { } tail)
                {
                    carried = true;
                    dc.PushOpacity(0.75);
                    Arc(dc, dotted, c, OuterRadius * k, tail, 1);
                    dc.Pop();
                }
                Arc(dc, outer, c, OuterRadius * k, s, Math.Max(e, s + MinSweep(k)));
                if (dim) dc.Pop();
            }
        if (carried)
        {
            var tick = new Pen(TickBrush, Math.Max(1.5, 1.5 * k * 1.6));
            dc.DrawLine(tick, At(c, (OuterRadius - OuterStroke / 2 - 4) * k, 0), At(c, (OuterRadius + OuterStroke / 2 + 1) * k, 0));
        }

        var total = Math.Clamp(TotalFraction, 0, 1);
        if (total > 0)
        {
            dc.PushOpacity(TotalOpacity);
            Arc(dc, Ring(SegmentBrush, InnerStroke), c, InnerRadius * k, 0, total);
            dc.Pop();
        }

        if (AverageFraction is { } avg1 && avg1 > 0)
        {
            var a = Math.Clamp(avg1, 0, 1) * 2 * Math.PI;
            var tick = new Pen(TickBrush, Math.Max(2, 3 * k * 1.6)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(tick, At(c, TickInner * k, a), At(c, TickOuter * k, a));
        }

        if (NowFraction is { } now1)
        {
            var a = Math.Clamp(now1, 0, 1) * 2 * Math.PI;
            var dot = Math.Max(4, 7 * k);
            dc.DrawEllipse(ChartSupport.Brush(this, "Brush.Panel"), new Pen(NowBrush, Math.Max(2, 4 * k)), At(c, OuterRadius * k, a), dot, dot);
        }

        var label1 = LabelBrush;
        var lsize1 = Math.Max(9, 14 * k);
        foreach (var h in new[] { 0, 6, 12, 18 })
        {
            var t = ChartSupport.Text(this, h.ToString("00"), NumberFont, lsize1, label1, FontWeights.Normal);
            var a = h / 24.0 * 2 * Math.PI;
            var p = At(c, (OuterRadius + OuterStroke / 2 + 9) * k, a);
            dc.DrawText(t, new Point(p.X - t.Width / 2, p.Y - t.Height / 2));
        }

        if (!string.IsNullOrEmpty(CenterText))
        {
            var t = ChartSupport.Text(this, CenterText!, NumberFont, 38 * k, CenterBrush, FontWeights.Bold);
            dc.DrawText(t, new Point(c.X - t.Width / 2, c.Y - t.Height / 2));
        }
    }

    private double? TailOf(int i) => Tails is { } t && i < t.Count ? t[i] : null;

    /// <summary>Shortest arc worth drawing (about one device pixel on the outer ring), so a short session never vanishes.</summary>
    private static double MinSweep(double k) => 1.5 / (2 * Math.PI * OuterRadius * k);

    private static Point At(Point c, double r, double angle) => new(c.X + r * Math.Sin(angle), c.Y - r * Math.Cos(angle));

    private static void Arc(DrawingContext dc, Pen pen, Point c, double r, double from, double to)
    {
        from = Math.Clamp(from, 0, 1);
        to = Math.Clamp(to, 0, 1);
        if (to <= from) return;
        if (to - from >= 0.9999) { dc.DrawEllipse(null, pen, c, r, r); return; }
        var a0 = from * 2 * Math.PI;
        var a1 = to * 2 * Math.PI;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(At(c, r, a0), false, false);
            ctx.ArcTo(At(c, r, a1), new Size(r, r), 0, a1 - a0 > Math.PI, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }
}
