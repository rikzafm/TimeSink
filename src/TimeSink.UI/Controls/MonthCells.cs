using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TimeSink.Core.Usage;

namespace TimeSink.UI.Controls;

/// <summary>
/// One month of the app detail year grid: 7 columns by up to 6 week rows of small cells (Monday or the chosen week start
/// first), each filled with its heat step; days before tracking or still to come are faint outlines. Colours are DPs the
/// host binds to theme brushes. Raises <see cref="HoverChanged"/> with the day under the pointer (null when it leaves).
/// </summary>
public sealed class MonthCells : FrameworkElement
{
    public const double Cell = 10, Gap = 2, Radius = 2.5;
    private const int Cols = 7, Rows = 6;

    private static DependencyProperty Reg<T>(string name, T def = default!) =>
        DependencyProperty.Register(name, typeof(T), typeof(MonthCells),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DaysProperty = Reg<IReadOnlyList<AppDetailDay?>?>(nameof(Days));
    public static readonly DependencyProperty HeatBrushesProperty = Reg<IReadOnlyList<Brush>?>(nameof(HeatBrushes));
    public static readonly DependencyProperty OutlineBrushProperty = Reg<Brush?>(nameof(OutlineBrush));

    /// <summary>42 slots in row-major order (week by week); null slots lie outside the month.</summary>
    public IReadOnlyList<AppDetailDay?>? Days { get => (IReadOnlyList<AppDetailDay?>?)GetValue(DaysProperty); set => SetValue(DaysProperty, value); }
    public IReadOnlyList<Brush>? HeatBrushes { get => (IReadOnlyList<Brush>?)GetValue(HeatBrushesProperty); set => SetValue(HeatBrushesProperty, value); }
    public Brush? OutlineBrush { get => (Brush?)GetValue(OutlineBrushProperty); set => SetValue(OutlineBrushProperty, value); }

    public event Action<AppDetailDay?>? HoverChanged;

    private AppDetailDay? _hover;

    protected override Size MeasureOverride(Size availableSize) =>
        new(Cols * Cell + (Cols - 1) * Gap, Rows * Cell + (Rows - 1) * Gap);

    private static Rect RectOf(int index) =>
        new(index % Cols * (Cell + Gap), index / Cols * (Cell + Gap), Cell, Cell);

    protected override void OnRender(DrawingContext dc)
    {
        ChartSupport.HitArea(dc, ActualWidth, ActualHeight);
        if (Days is not { } days || HeatBrushes is not { Count: >= 5 } heat) return;
        var pen = OutlineBrush is null ? null : new Pen(OutlineBrush, 1);
        for (var i = 0; i < days.Count && i < Cols * Rows; i++)
        {
            if (days[i] is not { } d) continue;
            var rect = RectOf(i);
            if (d.Kind == AppDetailDayKind.Tracked)
                dc.DrawRoundedRectangle(heat[Math.Clamp(d.Level, 0, 4)], null, rect, Radius, Radius);
            else
                dc.DrawRoundedRectangle(null, pen, new Rect(rect.X + 0.5, rect.Y + 0.5, rect.Width - 1, rect.Height - 1), Radius, Radius);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Set(DayAt(e.GetPosition(this)));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        Set(null);
    }

    private AppDetailDay? DayAt(Point p)
    {
        if (Days is not { } days) return null;
        var col = (int)(p.X / (Cell + Gap));
        var row = (int)(p.Y / (Cell + Gap));
        if (p.X < 0 || p.Y < 0 || col >= Cols || row >= Rows) return null;
        var i = row * Cols + col;
        return i < days.Count ? days[i] : null;
    }

    private void Set(AppDetailDay? day)
    {
        if (ReferenceEquals(day, _hover)) return;
        _hover = day;
        HoverChanged?.Invoke(day);
    }
}
