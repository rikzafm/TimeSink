using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TimeSink.UI.Controls;

/// <summary>One calendar cell: day number, heat step 0-4 and the pre-formatted hours text.</summary>
public sealed record CalendarCell(DateOnly Date, int Heat, string Hours, bool IsFuture, bool IsToday);

/// <summary>
/// Month calendar heatmap: weekday header, one row of seven heat cells per week plus a total column, the selected day
/// outlined, today underlined, future days dashed. Drawn with plain primitives; every colour and font is a DP that the
/// host binds to theme resources (<see cref="HeatBrushes"/> is set by the view from the <c>Brush.Heat*</c> tokens).
/// </summary>
public sealed class CalendarHeat : FrameworkElement
{
    private const double HeaderHeight = 18, CellHeight = 30, Gap = 3, TotalWidth = 40;

    private static DependencyProperty Reg<T>(string name, T def = default!) =>
        DependencyProperty.Register(name, typeof(T), typeof(CalendarHeat),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowsProperty = Reg<IReadOnlyList<IReadOnlyList<CalendarCell?>>?>(nameof(Rows));
    public static readonly DependencyProperty RowTotalsProperty = Reg<IReadOnlyList<string>?>(nameof(RowTotals));
    public static readonly DependencyProperty HeadersProperty = Reg<IReadOnlyList<string>?>(nameof(Headers));
    public static readonly DependencyProperty SelectedProperty = Reg<DateOnly?>(nameof(Selected));
    public static readonly DependencyProperty HeatBrushesProperty = Reg<IReadOnlyList<Brush>?>(nameof(HeatBrushes));
    public static readonly DependencyProperty TextBrushProperty = Reg<Brush?>(nameof(TextBrush));
    public static readonly DependencyProperty SoftBrushProperty = Reg<Brush?>(nameof(SoftBrush));
    public static readonly DependencyProperty MutedBrushProperty = Reg<Brush?>(nameof(MutedBrush));
    public static readonly DependencyProperty OnHeatBrushProperty = Reg<Brush?>(nameof(OnHeatBrush));
    public static readonly DependencyProperty FutureBorderBrushProperty = Reg<Brush?>(nameof(FutureBorderBrush));
    public static readonly DependencyProperty LabelFontFamilyProperty = Reg<FontFamily?>(nameof(LabelFontFamily));
    public static readonly DependencyProperty LabelFontSizeProperty = Reg<double>(nameof(LabelFontSize), 11.0);

    public IReadOnlyList<IReadOnlyList<CalendarCell?>>? Rows { get => (IReadOnlyList<IReadOnlyList<CalendarCell?>>?)GetValue(RowsProperty); set => SetValue(RowsProperty, value); }
    public IReadOnlyList<string>? RowTotals { get => (IReadOnlyList<string>?)GetValue(RowTotalsProperty); set => SetValue(RowTotalsProperty, value); }
    /// <summary>Eight labels: seven weekday names (in grid order) then the total column's title.</summary>
    public IReadOnlyList<string>? Headers { get => (IReadOnlyList<string>?)GetValue(HeadersProperty); set => SetValue(HeadersProperty, value); }
    public DateOnly? Selected { get => (DateOnly?)GetValue(SelectedProperty); set => SetValue(SelectedProperty, value); }
    public IReadOnlyList<Brush>? HeatBrushes { get => (IReadOnlyList<Brush>?)GetValue(HeatBrushesProperty); set => SetValue(HeatBrushesProperty, value); }
    public Brush? TextBrush { get => (Brush?)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush? SoftBrush { get => (Brush?)GetValue(SoftBrushProperty); set => SetValue(SoftBrushProperty, value); }
    public Brush? MutedBrush { get => (Brush?)GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }
    /// <summary>Text colour on the two brightest heat steps.</summary>
    public Brush? OnHeatBrush { get => (Brush?)GetValue(OnHeatBrushProperty); set => SetValue(OnHeatBrushProperty, value); }
    public Brush? FutureBorderBrush { get => (Brush?)GetValue(FutureBorderBrushProperty); set => SetValue(FutureBorderBrushProperty, value); }
    public FontFamily? LabelFontFamily { get => (FontFamily?)GetValue(LabelFontFamilyProperty); set => SetValue(LabelFontFamilyProperty, value); }
    public double LabelFontSize { get => (double)GetValue(LabelFontSizeProperty); set => SetValue(LabelFontSizeProperty, value); }

    /// <summary>Raised when a non-future day cell is clicked.</summary>
    public event Action<DateOnly>? DayClicked;

    protected override Size MeasureOverride(Size availableSize)
    {
        var rows = Rows?.Count ?? 0;
        return new Size(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            HeaderHeight + rows * (CellHeight + Gap));
    }

    private double CellWidth => Math.Max(1, (ActualWidth - TotalWidth - 7 * Gap) / 7);

    private Rect CellRect(int row, int col) =>
        new(col * (CellWidth + Gap), HeaderHeight + row * (CellHeight + Gap), CellWidth, CellHeight);

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight)); // hit-testable
        if (Rows is not { } rows || HeatBrushes is not { Count: >= 5 } heat) return;
        var cw = CellWidth;

        if (Headers is { Count: 8 } headers)
            for (var i = 0; i < 8; i++)
            {
                var x = i < 7 ? i * (cw + Gap) : 7 * (cw + Gap);
                var w = i < 7 ? cw : TotalWidth;
                Text(dc, headers[i], MutedBrush, FontWeights.SemiBold, new Rect(x, 0, w, HeaderHeight - 2), center: true);
            }

        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < 7 && c < rows[r].Count; c++)
            {
                if (rows[r][c] is not { } cell) continue;
                var rect = CellRect(r, c);
                var selected = Selected == cell.Date && !cell.IsFuture;
                if (cell.IsFuture)
                {
                    var pen = new Pen(FutureBorderBrush, 1) { DashStyle = DashStyles.Dash };
                    dc.DrawRoundedRectangle(null, pen, Deflate(rect, 0.5), 5, 5);
                }
                else
                {
                    dc.DrawRoundedRectangle(heat[Math.Clamp(cell.Heat, 0, 4)], null, rect, 5, 5);
                    if (cell.IsToday)
                    {
                        dc.PushClip(new RectangleGeometry(rect, 5, 5));
                        dc.DrawRectangle(TextBrush, null, new Rect(rect.Left, rect.Bottom - 3, rect.Width, 3));
                        dc.Pop();
                    }
                }
                if (selected)
                    dc.DrawRoundedRectangle(null, new Pen(TextBrush, 2), Inflate(rect, 1), 6, 6);

                var fg = cell.IsFuture ? MutedBrush : cell.Heat >= 3 ? OnHeatBrush : TextBrush;
                Text(dc, cell.Date.Day.ToString(CultureInfo.InvariantCulture), fg, FontWeights.Bold,
                    new Rect(rect.Left + 5, rect.Top + 3, rect.Width - 5, LabelFontSize + 2), center: false);
                if (cell.Hours.Length > 0)
                    Text(dc, cell.Hours, fg, FontWeights.Normal,
                        new Rect(rect.Left, rect.Bottom - LabelFontSize - 5, rect.Width - 5, LabelFontSize + 2), right: true);
            }
            if (RowTotals is { } totals && r < totals.Count && totals[r].Length > 0)
                Text(dc, totals[r], SoftBrush, FontWeights.SemiBold,
                    new Rect(7 * (cw + Gap), HeaderHeight + r * (CellHeight + Gap), TotalWidth, CellHeight), center: true, middle: true);
        }
    }

    private void Text(DrawingContext dc, string text, Brush? brush, FontWeight weight, Rect box, bool center = false, bool right = false, bool middle = false)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(LabelFontFamily ?? new FontFamily("Segoe UI"), FontStyles.Normal, weight, FontStretches.Normal),
            LabelFontSize, brush ?? Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip)
        { MaxTextWidth = Math.Max(1, box.Width), MaxLineCount = 1, Trimming = TextTrimming.None };
        var x = box.Left + (center ? (box.Width - ft.Width) / 2 : right ? box.Width - ft.Width : 0);
        var y = box.Top + (middle ? (box.Height - ft.Height) / 2 : 0);
        dc.DrawText(ft, new Point(x, y));
    }

    private static Rect Deflate(Rect r, double d) => new(r.Left + d, r.Top + d, Math.Max(0, r.Width - 2 * d), Math.Max(0, r.Height - 2 * d));
    private static Rect Inflate(Rect r, double d) => new(r.Left - d, r.Top - d, r.Width + 2 * d, r.Height + 2 * d);

    private CalendarCell? HitCell(Point p)
    {
        if (Rows is not { } rows) return null;
        var rowF = (p.Y - HeaderHeight) / (CellHeight + Gap);
        var colF = p.X / (CellWidth + Gap);
        if (rowF < 0 || colF < 0) return null;
        int row = (int)rowF, col = (int)colF;
        if (row >= rows.Count || col >= 7 || col >= rows[row].Count) return null;
        return CellRect(row, col).Contains(p) ? rows[row][col] : null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = HitCell(e.GetPosition(this)) is { IsFuture: false } ? Cursors.Hand : null;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (HitCell(e.GetPosition(this)) is { IsFuture: false } cell) DayClicked?.Invoke(cell.Date);
    }
}
