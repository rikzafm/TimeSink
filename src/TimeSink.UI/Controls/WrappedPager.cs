using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TimeSink.UI.Controls;

/// <summary>
/// Dynamic sliding-window pager for Wrapped cards with animated active pill,
/// progressive edge dot scaling, and dark-filled navigation buttons matching the mobile carousel style.
/// </summary>
public sealed class WrappedPager : FrameworkElement
{
    private const double ButtonSize = 32.0;
    private const double ButtonGap = 8.0;
    private const double DotDiameter = 6.0;
    private const double ActiveDotWidth = 20.0;
    private const double DotSpacing = 6.0;
    private const double MaxViewportWidth = 92.0;

    private static readonly Brush DefaultDotBrush = new SolidColorBrush(Color.FromRgb(0x62, 0x73, 0x52));

    private static DependencyProperty Reg<T>(string name, T def = default!) =>
        DependencyProperty.Register(name, typeof(T), typeof(WrappedPager),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CountProperty =
        DependencyProperty.Register(nameof(Count), typeof(int), typeof(WrappedPager),
            new FrameworkPropertyMetadata(3, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IndexProperty =
        DependencyProperty.Register(nameof(Index), typeof(int), typeof(WrappedPager),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender, OnIndexChanged));

    public static readonly DependencyProperty AnimatedIndexProperty =
        DependencyProperty.Register(nameof(AnimatedIndex), typeof(double), typeof(WrappedPager),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LineBrushProperty = Reg<Brush?>(nameof(LineBrush));
    public static readonly DependencyProperty IconBrushProperty = Reg<Brush?>(nameof(IconBrush));
    public static readonly DependencyProperty DotBrushProperty = Reg<Brush?>(nameof(DotBrush));
    public static readonly DependencyProperty ActiveBrushProperty = Reg<Brush?>(nameof(ActiveBrush));
    public static readonly DependencyProperty ButtonBackgroundBrushProperty = Reg<Brush?>(nameof(ButtonBackgroundBrush));
    public static readonly DependencyProperty ButtonRadiusProperty = Reg(nameof(ButtonRadius), 8.0);

    public int Count { get => (int)GetValue(CountProperty); set => SetValue(CountProperty, value); }
    public int Index { get => (int)GetValue(IndexProperty); set => SetValue(IndexProperty, value); }
    public double AnimatedIndex { get => (double)GetValue(AnimatedIndexProperty); set => SetValue(AnimatedIndexProperty, value); }
    public Brush? LineBrush { get => (Brush?)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public Brush? IconBrush { get => (Brush?)GetValue(IconBrushProperty); set => SetValue(IconBrushProperty, value); }
    public Brush? DotBrush { get => (Brush?)GetValue(DotBrushProperty); set => SetValue(DotBrushProperty, value); }
    public Brush? ActiveBrush { get => (Brush?)GetValue(ActiveBrushProperty); set => SetValue(ActiveBrushProperty, value); }
    public Brush? ButtonBackgroundBrush { get => (Brush?)GetValue(ButtonBackgroundBrushProperty); set => SetValue(ButtonBackgroundBrushProperty, value); }
    public double ButtonRadius { get => (double)GetValue(ButtonRadiusProperty); set => SetValue(ButtonRadiusProperty, value); }

    /// <summary>Raised with the requested card index (already clamped and different from <see cref="Index"/>).</summary>
    public event Action<int>? PageRequested;

    private double DisabledOpacity => TryFindResource("Double.Opacity.Disabled") is double d ? d : 0.35;

    public WrappedPager()
    {
        Cursor = Cursors.Hand;
        Focusable = false;
        Loaded += (_, _) => SetCurrentValue(AnimatedIndexProperty, (double)Index);
    }

    private static void OnIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not WrappedPager pager) return;
        var oldVal = pager.AnimatedIndex;
        var newVal = (double)(int)e.NewValue;
        if (!pager.IsLoaded)
        {
            pager.SetCurrentValue(AnimatedIndexProperty, newVal);
            return;
        }

        var anim = new DoubleAnimation
        {
            From = oldVal,
            To = newVal,
            Duration = new Duration(TimeSpan.FromMilliseconds(200)),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        pager.BeginAnimation(AnimatedIndexProperty, anim, HandoffBehavior.SnapshotAndReplace);
    }

    private double CalculateViewportWidth(int count, out double totalTrackWidth)
    {
        if (count <= 0)
        {
            totalTrackWidth = 0;
            return 0;
        }
        totalTrackWidth = (count - 1) * (DotDiameter + DotSpacing) + ActiveDotWidth;
        return Math.Min(MaxViewportWidth, totalTrackWidth);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var vpWidth = CalculateViewportWidth(Count, out _);
        var totalWidth = ButtonSize + ButtonGap + vpWidth + ButtonGap + ButtonSize;
        return new Size(double.IsInfinity(availableSize.Width) ? totalWidth : Math.Min(availableSize.Width, totalWidth), ButtonSize);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var p = e.GetPosition(this);
        var vpWidth = CalculateViewportWidth(Count, out var totalTrackWidth);
        var vpLeft = ButtonSize + ButtonGap;
        var rightButtonLeft = vpLeft + vpWidth + ButtonGap;

        if (p.X <= ButtonSize)
        {
            Request(Index - 1);
            return;
        }
        if (p.X >= rightButtonLeft && p.X <= rightButtonLeft + ButtonSize)
        {
            Request(Index + 1);
            return;
        }
        if (p.X >= vpLeft && p.X <= vpLeft + vpWidth && Count > 0)
        {
            ComputeTrack(AnimatedIndex, Count, vpWidth, totalTrackWidth, out var centers, out _, out var scrollOffset);
            var clickTrackX = (p.X - vpLeft) + scrollOffset;
            var bestIdx = -1;
            var bestDist = double.MaxValue;
            for (var i = 0; i < Count; i++)
            {
                var dist = Math.Abs(centers[i] - clickTrackX);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = i;
                }
            }
            if (bestIdx >= 0 && bestDist <= DotDiameter + DotSpacing)
            {
                Request(bestIdx);
            }
        }
    }

    private void Request(int i)
    {
        i = Math.Clamp(i, 0, Math.Max(0, Count - 1));
        if (i != Index) PageRequested?.Invoke(i);
    }

    private static void ComputeTrack(double animatedIdx, int count, double vpWidth, double totalTrackWidth,
        out double[] centers, out double[] widths, out double scrollOffset)
    {
        centers = new double[count];
        widths = new double[count];
        if (count == 0)
        {
            scrollOffset = 0;
            return;
        }

        var p = Math.Clamp(animatedIdx, 0, count - 1);
        for (var i = 0; i < count; i++)
        {
            var dist = Math.Abs(i - p);
            var t = Math.Max(0.0, 1.0 - dist);
            widths[i] = DotDiameter + t * (ActiveDotWidth - DotDiameter);
        }

        centers[0] = widths[0] / 2.0;
        for (var i = 1; i < count; i++)
        {
            centers[i] = centers[i - 1] + widths[i - 1] / 2.0 + DotSpacing + widths[i] / 2.0;
        }

        var floorP = (int)Math.Floor(p);
        var ceilP = (int)Math.Ceiling(p);
        var frac = p - floorP;
        var activeCenter = centers[floorP] + (ceilP < count ? frac * (centers[ceilP] - centers[floorP]) : 0);

        if (totalTrackWidth <= vpWidth)
        {
            scrollOffset = -(vpWidth - totalTrackWidth) / 2.0;
        }
        else
        {
            scrollOffset = Math.Clamp(activeCenter - vpWidth / 2.0, 0, totalTrackWidth - vpWidth);
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var vpWidth = CalculateViewportWidth(Count, out var totalTrackWidth);
        var totalW = ButtonSize + ButtonGap + vpWidth + ButtonGap + ButtonSize;
        ChartSupport.HitArea(dc, totalW, ButtonSize);

        var bgBrush = ButtonBackgroundBrush ?? TryFindResource("Brush.SurfaceAlt") as Brush;
        var borderBrush = LineBrush ?? TryFindResource("Brush.Border") as Brush;
        var pen = new Pen(borderBrush, 1);
        var radius = ButtonRadius > 0 ? ButtonRadius : 8.0;

        // 1. Previous button
        DrawButton(dc, bgBrush, pen, new Rect(0.5, 0.5, ButtonSize - 1, ButtonSize - 1), radius, left: true, enabled: Index > 0);

        // 2. Right button
        var rightX = ButtonSize + ButtonGap + vpWidth + ButtonGap;
        DrawButton(dc, bgBrush, pen, new Rect(rightX + 0.5, 0.5, ButtonSize - 1, ButtonSize - 1), radius, left: false, enabled: Index < Count - 1);

        // 3. Dynamic dots
        if (Count <= 0) return;

        ComputeTrack(AnimatedIndex, Count, vpWidth, totalTrackWidth, out var centers, out var widths, out var scrollOffset);

        var vpLeft = ButtonSize + ButtonGap;
        dc.PushClip(new RectangleGeometry(new Rect(vpLeft, 0, vpWidth, ButtonSize)));

        var p = Math.Clamp(AnimatedIndex, 0, Count - 1);
        var maxScroll = Math.Max(0, totalTrackWidth - vpWidth);
        var leftShrink = maxScroll > 0.5 ? Math.Min(1.0, Math.Max(0.0, scrollOffset) / 10.0) : 0.0;
        var rightShrink = maxScroll > 0.5 ? Math.Min(1.0, Math.Max(0.0, maxScroll - scrollOffset) / 10.0) : 0.0;

        var dotBaseBrush = DotBrush ?? DefaultDotBrush;

        for (var i = 0; i < Count; i++)
        {
            var cx = centers[i] - scrollOffset;
            var w = widths[i];
            if (cx + w / 2 < -4 || cx - w / 2 > vpWidth + 4) continue;

            // Progressive scale tapering at viewport boundaries
            var scale = 1.0;
            if (maxScroll > 0.5)
            {
                var scaleL = 1.0;
                if (leftShrink > 0)
                {
                    if (cx < 6) scaleL = 0.42;
                    else if (cx < 18) scaleL = 0.42 + (cx - 6) / 12.0 * (1.0 - 0.42);
                    scaleL = 1.0 - (1.0 - scaleL) * leftShrink;
                }

                var scaleR = 1.0;
                var dR = vpWidth - cx;
                if (rightShrink > 0)
                {
                    if (dR < 6) scaleR = 0.42;
                    else if (dR < 18) scaleR = 0.42 + (dR - 6) / 12.0 * (1.0 - 0.42);
                    scaleR = 1.0 - (1.0 - scaleR) * rightShrink;
                }

                scale = Math.Min(scaleL, scaleR);
            }

            var finalW = Math.Max(2.0, w * scale);
            var finalH = Math.Max(2.0, DotDiameter * scale);
            var r = new Rect(vpLeft + cx - finalW / 2.0, (ButtonSize - finalH) / 2.0, finalW, finalH);
            var cr = finalH / 2.0;

            var dist = Math.Abs(i - p);
            var activeFraction = Math.Max(0.0, 1.0 - dist);

            // Inactive base dot in rich olive tone
            dc.DrawRoundedRectangle(dotBaseBrush, null, r, cr, cr);

            // Active pill overlay with smooth alpha transition
            if (activeFraction > 0.01 && ActiveBrush != null)
            {
                dc.PushOpacity(activeFraction);
                dc.DrawRoundedRectangle(ActiveBrush, null, r, cr, cr);
                dc.Pop();
            }
        }

        dc.Pop(); // Pop clip
    }

    private void DrawButton(DrawingContext dc, Brush? bg, Pen pen, Rect r, double radius, bool left, bool enabled)
    {
        dc.PushOpacity(enabled ? 1.0 : DisabledOpacity);
        dc.DrawRoundedRectangle(bg, pen, r, radius, radius);

        var cx = r.X + r.Width / 2.0;
        var cy = r.Y + r.Height / 2.0;
        var d = left ? -1 : 1;

        var chevron = new StreamGeometry();
        using (var c = chevron.Open())
        {
            c.BeginFigure(new Point(cx - 3 * d, cy - 5.5), false, false);
            c.LineTo(new Point(cx + 3 * d, cy), true, true);
            c.LineTo(new Point(cx - 3 * d, cy + 5.5), true, true);
        }
        chevron.Freeze();

        var iconPen = new Pen(IconBrush, 2.0)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        dc.DrawGeometry(null, iconPen, chevron);

        dc.Pop();
    }
}
