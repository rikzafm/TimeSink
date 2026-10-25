using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace TimeSink.UI.Controls;

/// <summary>One matrix row as drawn: app name, its palette slot (the swatch shown when there is no <c>Icon</c>), cell texts with colour levels 0-4, and the row total text.</summary>
public sealed record MatrixGridRow(string Name, int PaletteSlot, IReadOnlyList<string> CellText, IReadOnlyList<int> Levels, string Total, ImageSource? Icon = null);

/// <summary>
/// The Year app x month matrix: a header of month buttons, one row per app of heat cells with a total column, and an
/// "all apps" totals row. Drawn with plain primitives; every colour and font is a DP bound to theme resources by the host.
/// A row (app name) or a month header can be tapped (<see cref="RowClicked"/>, <see cref="MonthClicked"/>).
/// </summary>
public sealed class MatrixGrid : FrameworkElement
{
    private const double HeaderHeight = 22, RowHeight = 32, Gap = 3, TotalWidth = 44, FooterGap = 6, FooterHeight = 24, IconSize = 16, NameInset = 22;

    private static DependencyProperty Reg<T>(string name, T def = default!) =>
        DependencyProperty.Register(name, typeof(T), typeof(MatrixGrid),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty MonthLabelsProperty = Reg<IReadOnlyList<string>?>(nameof(MonthLabels));
    public static readonly DependencyProperty RowsProperty = Reg<IReadOnlyList<MatrixGridRow>?>(nameof(Rows));
    public static readonly DependencyProperty ColumnTotalsProperty = Reg<IReadOnlyList<string>?>(nameof(ColumnTotals));
    public static readonly DependencyProperty TotalHeaderProperty = Reg<string?>(nameof(TotalHeader));
    public static readonly DependencyProperty AllAppsLabelProperty = Reg<string?>(nameof(AllAppsLabel));
    public static readonly DependencyProperty GrandTotalProperty = Reg<string?>(nameof(GrandTotal));
    public static readonly DependencyProperty SelectedRowProperty = Reg(nameof(SelectedRow), -1);
    public static readonly DependencyProperty SelectedMonthProperty = Reg(nameof(SelectedMonth), -1);
    public static readonly DependencyProperty HeatBrushesProperty = Reg<IReadOnlyList<Brush>?>(nameof(HeatBrushes));
    public static readonly DependencyProperty AppBrushesProperty = Reg<IReadOnlyList<Brush>?>(nameof(AppBrushes));
    public static readonly DependencyProperty TextBrushProperty = Reg<Brush?>(nameof(TextBrush));
    public static readonly DependencyProperty MutedBrushProperty = Reg<Brush?>(nameof(MutedBrush));
    public static readonly DependencyProperty AccentBrushProperty = Reg<Brush?>(nameof(AccentBrush));
    public static readonly DependencyProperty OnHeatBrushProperty = Reg<Brush?>(nameof(OnHeatBrush));
    public static readonly DependencyProperty OnSelectionBrushProperty = Reg<Brush?>(nameof(OnSelectionBrush));
    public static readonly DependencyProperty BorderBrushLineProperty = Reg<Brush?>(nameof(BorderBrushLine));
    public static readonly DependencyProperty LabelFontFamilyProperty = Reg<FontFamily?>(nameof(LabelFontFamily));
    public static readonly DependencyProperty LabelFontSizeProperty = Reg<double>(nameof(LabelFontSize), 11.0);

    public IReadOnlyList<string>? MonthLabels { get => (IReadOnlyList<string>?)GetValue(MonthLabelsProperty); set => SetValue(MonthLabelsProperty, value); }
    public IReadOnlyList<MatrixGridRow>? Rows { get => (IReadOnlyList<MatrixGridRow>?)GetValue(RowsProperty); set => SetValue(RowsProperty, value); }
    public IReadOnlyList<string>? ColumnTotals { get => (IReadOnlyList<string>?)GetValue(ColumnTotalsProperty); set => SetValue(ColumnTotalsProperty, value); }
    public string? TotalHeader { get => (string?)GetValue(TotalHeaderProperty); set => SetValue(TotalHeaderProperty, value); }
    public string? AllAppsLabel { get => (string?)GetValue(AllAppsLabelProperty); set => SetValue(AllAppsLabelProperty, value); }
    public string? GrandTotal { get => (string?)GetValue(GrandTotalProperty); set => SetValue(GrandTotalProperty, value); }
    public int SelectedRow { get => (int)GetValue(SelectedRowProperty); set => SetValue(SelectedRowProperty, value); }
    public int SelectedMonth { get => (int)GetValue(SelectedMonthProperty); set => SetValue(SelectedMonthProperty, value); }
    public IReadOnlyList<Brush>? HeatBrushes { get => (IReadOnlyList<Brush>?)GetValue(HeatBrushesProperty); set => SetValue(HeatBrushesProperty, value); }
    public IReadOnlyList<Brush>? AppBrushes { get => (IReadOnlyList<Brush>?)GetValue(AppBrushesProperty); set => SetValue(AppBrushesProperty, value); }
    public Brush? TextBrush { get => (Brush?)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
    public Brush? MutedBrush { get => (Brush?)GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }
    public Brush? AccentBrush { get => (Brush?)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    /// <summary>Text colour on the two brightest heat steps.</summary>
    public Brush? OnHeatBrush { get => (Brush?)GetValue(OnHeatBrushProperty); set => SetValue(OnHeatBrushProperty, value); }
    /// <summary>Text colour on the selected month header (which is filled with <see cref="TextBrush"/>).</summary>
    public Brush? OnSelectionBrush { get => (Brush?)GetValue(OnSelectionBrushProperty); set => SetValue(OnSelectionBrushProperty, value); }
    public Brush? BorderBrushLine { get => (Brush?)GetValue(BorderBrushLineProperty); set => SetValue(BorderBrushLineProperty, value); }
    public FontFamily? LabelFontFamily { get => (FontFamily?)GetValue(LabelFontFamilyProperty); set => SetValue(LabelFontFamilyProperty, value); }
    public double LabelFontSize { get => (double)GetValue(LabelFontSizeProperty); set => SetValue(LabelFontSizeProperty, value); }

    public event Action<int>? RowClicked;
    public event Action<int>? MonthClicked;

    private int Columns => MonthLabels?.Count ?? 0;
    private int RowCount => Rows?.Count ?? 0;
    private double NameWidth => 70;
    private double CellWidth => Columns == 0 ? 0 : Math.Max(1, (ActualWidth - NameWidth - TotalWidth - (Columns + 1) * Gap) / Columns);
    private double CellX(int col) => NameWidth + Gap + col * (CellWidth + Gap);
    private double RowY(int row) => HeaderHeight + Gap + row * (RowHeight + Gap);
    private double FooterY => RowY(RowCount) + FooterGap - Gap;

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, FooterY + FooterHeight + 2);

    protected override void OnRender(DrawingContext dc)
    {
        ChartSupport.HitArea(dc, ActualWidth, ActualHeight);
        if (Rows is not { } rows || MonthLabels is not { } months || HeatBrushes is not { Count: >= 5 } heat || ActualWidth <= 0) return;
        var cw = CellWidth;

        for (var m = 0; m < months.Count; m++)
        {
            var rect = new Rect(CellX(m), 0, cw, HeaderHeight);
            var on = SelectedMonth == m;
            if (on) dc.DrawRoundedRectangle(TextBrush, null, rect, 5, 5);
            Text(dc, months[m], on ? OnSelectionBrush : MutedBrush, FontWeights.Bold, rect, center: true, middle: true, delta: -1);
        }
        Text(dc, TotalHeader ?? "", MutedBrush, FontWeights.Bold,
            new Rect(ActualWidth - TotalWidth, 0, TotalWidth, HeaderHeight), right: true, middle: true, delta: -1);

        for (var r = 0; r < rows.Count; r++)
        {
            var row = rows[r];
            var y = RowY(r);
            if (SelectedRow == r)
                dc.DrawRoundedRectangle(null, new Pen(TextBrush, 1), new Rect(-1, y - 1, ActualWidth + 2, RowHeight + 2), 6, 6);
            if (row.Icon is { } icon)
                dc.DrawImage(icon, new Rect(0, y + (RowHeight - IconSize) / 2, IconSize, IconSize));
            else if (AppBrushes is { Count: > 0 } apps)
                dc.DrawRoundedRectangle(apps[row.PaletteSlot % apps.Count], null, new Rect(2, y + (RowHeight - 8) / 2, 8, 8), 2, 2);
            Text(dc, row.Name, TextBrush, FontWeights.SemiBold, new Rect(NameInset, y, NameWidth - NameInset, RowHeight), middle: true, trim: true, delta: 1);

            for (var m = 0; m < months.Count && m < row.CellText.Count; m++)
            {
                var level = Math.Clamp(row.Levels[m], 0, 4);
                var rect = new Rect(CellX(m), y, cw, RowHeight);
                dc.DrawRoundedRectangle(heat[level], null, rect, 5, 5);
                if (SelectedMonth == m) dc.DrawRoundedRectangle(null, new Pen(TextBrush, 2), Inflate(rect, -1), 5, 5);
                Text(dc, row.CellText[m], level >= 3 ? OnHeatBrush : TextBrush, FontWeights.SemiBold, rect, center: true, middle: true);
            }
            Text(dc, row.Total, TextBrush, FontWeights.Bold, new Rect(ActualWidth - TotalWidth, y, TotalWidth, RowHeight), right: true, middle: true, delta: 1);
        }

        var fy = FooterY;
        dc.DrawLine(new Pen(BorderBrushLine, 1), new Point(0, fy), new Point(ActualWidth, fy));
        var fr = new Rect(0, fy + 1, NameWidth, FooterHeight);
        Text(dc, AllAppsLabel ?? "", MutedBrush, FontWeights.Bold, fr, middle: true);
        if (ColumnTotals is { } totals)
            for (var m = 0; m < months.Count && m < totals.Count; m++)
                Text(dc, totals[m], SelectedMonth == m ? TextBrush : MutedBrush, FontWeights.Bold,
                    new Rect(CellX(m), fy + 1, cw, FooterHeight), center: true, middle: true);
        Text(dc, GrandTotal ?? "", AccentBrush, FontWeights.Bold, new Rect(ActualWidth - TotalWidth, fy + 1, TotalWidth, FooterHeight), right: true, middle: true, delta: 1);
    }

    private void Text(DrawingContext dc, string text, Brush? brush, FontWeight weight, Rect box,
        bool center = false, bool right = false, bool middle = false, bool trim = false, double delta = 0)
    {
        if (text.Length == 0) return;
        var ft = ChartSupport.Text(this, text, LabelFontFamily, LabelFontSize + delta, brush, weight);
        ft.MaxLineCount = 1;
        ft.Trimming = trim ? TextTrimming.CharacterEllipsis : TextTrimming.None;
        if (trim) ft.MaxTextWidth = Math.Max(1, box.Width);
        var x = box.Left + (center ? (box.Width - ft.Width) / 2 : right ? box.Width - ft.Width : 0);
        var y = box.Top + (middle ? (box.Height - ft.Height) / 2 : 0);
        dc.DrawText(ft, new Point(x, y));
    }

    private static Rect Inflate(Rect r, double d) => ChartSupport.Inflate(r, d);

    /// <summary>What is under the point: a month header, an app row (name cell), or nothing.</summary>
    private (int Month, int Row) Hit(Point p)
    {
        if (p.Y < HeaderHeight + Gap && Columns > 0)
        {
            var c = (int)Math.Floor((p.X - NameWidth - Gap + Gap / 2) / (CellWidth + Gap));
            return (c >= 0 && c < Columns ? c : -1, -1);
        }
        var r = (int)Math.Floor((p.Y - HeaderHeight - Gap) / (RowHeight + Gap));
        return r >= 0 && r < RowCount ? (-1, r) : (-1, -1);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var (m, r) = Hit(e.GetPosition(this));
        Cursor = m >= 0 || r >= 0 ? Cursors.Hand : null;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var (m, r) = Hit(e.GetPosition(this));
        if (m >= 0) MonthClicked?.Invoke(m);
        else if (r >= 0) RowClicked?.Invoke(r);
    }
}
