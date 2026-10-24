using System.Windows;
using System.Windows.Media;
using TimeSink.Core.Usage;

namespace TimeSink.UI.Controls;

/// <summary>
/// The 20-week heat grid: 20 week columns by 7 day rows (cells arrive week-major), each cell filled with the heat step
/// for its day; days still to come are drawn as a faint outline. Colours are DPs bound to theme brushes.
/// </summary>
public sealed class HeatGrid : FrameworkElement
{
    private const int Weeks = WeekQuery.GridWeeks, Rows = 7;
    private const double Gap = 3;

    private static DependencyProperty Reg<T>(string name, T def = default!) =>
        DependencyProperty.Register(name, typeof(T), typeof(HeatGrid),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CellsProperty = Reg<IReadOnlyList<HeatCell>?>(nameof(Cells));
    public static readonly DependencyProperty Level0BrushProperty = Reg<Brush?>(nameof(Level0Brush));
    public static readonly DependencyProperty Level1BrushProperty = Reg<Brush?>(nameof(Level1Brush));
    public static readonly DependencyProperty Level2BrushProperty = Reg<Brush?>(nameof(Level2Brush));
    public static readonly DependencyProperty Level3BrushProperty = Reg<Brush?>(nameof(Level3Brush));
    public static readonly DependencyProperty Level4BrushProperty = Reg<Brush?>(nameof(Level4Brush));
    public static readonly DependencyProperty FutureBrushProperty = Reg<Brush?>(nameof(FutureBrush));

    public IReadOnlyList<HeatCell>? Cells { get => (IReadOnlyList<HeatCell>?)GetValue(CellsProperty); set => SetValue(CellsProperty, value); }
    public Brush? Level0Brush { get => (Brush?)GetValue(Level0BrushProperty); set => SetValue(Level0BrushProperty, value); }
    public Brush? Level1Brush { get => (Brush?)GetValue(Level1BrushProperty); set => SetValue(Level1BrushProperty, value); }
    public Brush? Level2Brush { get => (Brush?)GetValue(Level2BrushProperty); set => SetValue(Level2BrushProperty, value); }
    public Brush? Level3Brush { get => (Brush?)GetValue(Level3BrushProperty); set => SetValue(Level3BrushProperty, value); }
    public Brush? Level4Brush { get => (Brush?)GetValue(Level4BrushProperty); set => SetValue(Level4BrushProperty, value); }
    public Brush? FutureBrush { get => (Brush?)GetValue(FutureBrushProperty); set => SetValue(FutureBrushProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var w = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
        return new Size(w, CellSize(w) * Rows + Gap * (Rows - 1));
    }

    private static double CellSize(double width) => Math.Max(1, (width - Gap * (Weeks - 1)) / Weeks);

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        if (Cells is not { Count: Weeks * Rows } cells || w <= 0) return;
        var s = CellSize(w);
        Brush?[] levels = [Level0Brush, Level1Brush, Level2Brush, Level3Brush, Level4Brush];
        for (var i = 0; i < cells.Count; i++)
        {
            var cell = cells[i];
            var rect = new Rect(i / Rows * (s + Gap), i % Rows * (s + Gap), s, s);
            if (cell.IsFuture) dc.DrawRoundedRectangle(null, FutureBrush is null ? null : new Pen(FutureBrush, 1), rect, 2.5, 2.5);
            else dc.DrawRoundedRectangle(levels[Math.Clamp(cell.Level, 0, 4)], null, rect, 2.5, 2.5);
        }
    }
}
