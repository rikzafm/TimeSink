using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TimeSink.UI.Controls;

/// <summary>Previous button, page dots (active one is a wide pill) and next button for the Wrapped cards.</summary>
public sealed class WrappedPager : FrameworkElement
{
    private const double Button = 36, Dot = 8, ActiveDot = 24, DotGap = 8;

    private static DependencyProperty Reg<T>(string name, T def = default!) =>
        DependencyProperty.Register(name, typeof(T), typeof(WrappedPager),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CountProperty = Reg(nameof(Count), 3);
    public static readonly DependencyProperty IndexProperty = Reg(nameof(Index), 0);
    public static readonly DependencyProperty LineBrushProperty = Reg<Brush?>(nameof(LineBrush));
    public static readonly DependencyProperty IconBrushProperty = Reg<Brush?>(nameof(IconBrush));
    public static readonly DependencyProperty DotBrushProperty = Reg<Brush?>(nameof(DotBrush));
    public static readonly DependencyProperty ActiveBrushProperty = Reg<Brush?>(nameof(ActiveBrush));
    public static readonly DependencyProperty ButtonRadiusProperty = Reg(nameof(ButtonRadius), 8.0);

    public int Count { get => (int)GetValue(CountProperty); set => SetValue(CountProperty, value); }
    public int Index { get => (int)GetValue(IndexProperty); set => SetValue(IndexProperty, value); }
    public Brush? LineBrush { get => (Brush?)GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public Brush? IconBrush { get => (Brush?)GetValue(IconBrushProperty); set => SetValue(IconBrushProperty, value); }
    public Brush? DotBrush { get => (Brush?)GetValue(DotBrushProperty); set => SetValue(DotBrushProperty, value); }
    public Brush? ActiveBrush { get => (Brush?)GetValue(ActiveBrushProperty); set => SetValue(ActiveBrushProperty, value); }
    public double ButtonRadius { get => (double)GetValue(ButtonRadiusProperty); set => SetValue(ButtonRadiusProperty, value); }

    /// <summary>Raised with the requested card index (already clamped and different from <see cref="Index"/>).</summary>
    public event Action<int>? PageRequested;

    public WrappedPager() { Cursor = Cursors.Hand; Focusable = false; }

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, Button);

    private double DotsWidth => Math.Max(0, Count - 1) * (Dot + DotGap) + ActiveDot;

    private double DotX(int i) => (ActualWidth - DotsWidth) / 2 + i * (Dot + DotGap) + (i > Index ? ActiveDot - Dot : 0);

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var p = e.GetPosition(this);
        if (p.X <= Button) Request(Index - 1);
        else if (p.X >= ActualWidth - Button) Request(Index + 1);
        else
            for (var i = 0; i < Count; i++)
            {
                var x = DotX(i);
                if (p.X >= x - DotGap / 2 && p.X <= x + (i == Index ? ActiveDot : Dot) + DotGap / 2) { Request(i); break; }
            }
    }

    private void Request(int i)
    {
        i = Math.Clamp(i, 0, Count - 1);
        if (i != Index) PageRequested?.Invoke(i);
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, Button));
        var pen = new Pen(LineBrush, 1);
        DrawButton(dc, pen, new Rect(0.5, 0.5, Button - 1, Button - 1), left: true, enabled: Index > 0);
        DrawButton(dc, pen, new Rect(w - Button + 0.5, 0.5, Button - 1, Button - 1), left: false, enabled: Index < Count - 1);
        for (var i = 0; i < Count; i++)
        {
            var active = i == Index;
            dc.DrawRoundedRectangle(active ? ActiveBrush : DotBrush, null, new Rect(DotX(i), (Button - Dot) / 2, active ? ActiveDot : Dot, Dot), 4, 4);
        }
    }

    private void DrawButton(DrawingContext dc, Pen pen, Rect r, bool left, bool enabled)
    {
        dc.PushOpacity(enabled ? 1 : 0.35);
        dc.DrawRoundedRectangle(null, pen, r, ButtonRadius, ButtonRadius);
        var cx = r.X + r.Width / 2;
        var cy = r.Y + r.Height / 2;
        var d = left ? -1 : 1;
        var chevron = new StreamGeometry();
        using (var c = chevron.Open())
        {
            c.BeginFigure(new Point(cx - 3 * d, cy - 6), false, false);
            c.LineTo(new Point(cx + 3 * d, cy), true, true);
            c.LineTo(new Point(cx - 3 * d, cy + 6), true, true);
        }
        chevron.Freeze();
        dc.DrawGeometry(null, new Pen(IconBrush, 2.2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, chevron);
        dc.Pop();
    }
}
