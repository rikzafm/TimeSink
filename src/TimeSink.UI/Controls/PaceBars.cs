using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using TimeSink.Core.Usage;

namespace TimeSink.UI.Controls;

/// <summary>
/// The Year pace chart: 12 monthly columns with a value label on top, a hatched projected extension on the current
/// month, dashed ghost bars for months to come, dotted stubs before tracking began and a dashed average line. Clicking a
/// column raises <see cref="MonthPicked"/>. Plain primitives; every colour/font is a DP bound to a theme resource.
/// </summary>
public sealed class PaceBars : FrameworkElement
{
    private const double ValueBand = 16, MonthBand = 18, Gap = 3;

    private static DependencyProperty Reg<T>(string name, T def = default!) =>
        DependencyProperty.Register(name, typeof(T), typeof(PaceBars),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MonthsProperty = Reg<IReadOnlyList<YearMonth>?>(nameof(Months));
    public static readonly DependencyProperty ValueLabelsProperty = Reg<IReadOnlyList<string>?>(nameof(ValueLabels));
    public static readonly DependencyProperty MonthLabelsProperty = Reg<IReadOnlyList<string>?>(nameof(MonthLabels));
    public static readonly DependencyProperty AverageSecondsProperty = Reg<long?>(nameof(AverageSeconds));
    public static readonly DependencyProperty SelectedIndexProperty = Reg<int>(nameof(SelectedIndex), -1);
    public static readonly DependencyProperty BarBrushProperty = Reg<Brush?>(nameof(BarBrush));
    public static readonly DependencyProperty CurrentBrushProperty = Reg<Brush?>(nameof(CurrentBrush));
    public static readonly DependencyProperty ProjectedBrushProperty = Reg<Brush?>(nameof(ProjectedBrush));
    public static readonly DependencyProperty MutedBrushProperty = Reg<Brush?>(nameof(MutedBrush));
    public static readonly DependencyProperty LabelBrushProperty = Reg<Brush?>(nameof(LabelBrush));
    public static readonly DependencyProperty SelectedLabelBrushProperty = Reg<Brush?>(nameof(SelectedLabelBrush));
    public static readonly DependencyProperty SelectedBackBrushProperty = Reg<Brush?>(nameof(SelectedBackBrush));
    public static readonly DependencyProperty BaselineBrushProperty = Reg<Brush?>(nameof(BaselineBrush));
    public static readonly DependencyProperty LabelFontFamilyProperty = Reg<FontFamily?>(nameof(LabelFontFamily));
    public static readonly DependencyProperty LabelFontSizeProperty = Reg<double>(nameof(LabelFontSize), 10.0);

    public IReadOnlyList<YearMonth>? Months { get => (IReadOnlyList<YearMonth>?)GetValue(MonthsProperty); set => SetValue(MonthsProperty, value); }
    /// <summary>One pre-composed text per column, drawn above the bar (e.g. "58", "~43", "–").</summary>
    public IReadOnlyList<string>? ValueLabels { get => (IReadOnlyList<string>?)GetValue(ValueLabelsProperty); set => SetValue(ValueLabelsProperty, value); }
    public IReadOnlyList<string>? MonthLabels { get => (IReadOnlyList<string>?)GetValue(MonthLabelsProperty); set => SetValue(MonthLabelsProperty, value); }
    public long? AverageSeconds { get => (long?)GetValue(AverageSecondsProperty); set => SetValue(AverageSecondsProperty, value); }
    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }
    public Brush? BarBrush { get => (Brush?)GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
    public Brush? CurrentBrush { get => (Brush?)GetValue(CurrentBrushProperty); set => SetValue(CurrentBrushProperty, value); }
    public Brush? ProjectedBrush { get => (Brush?)GetValue(ProjectedBrushProperty); set => SetValue(ProjectedBrushProperty, value); }
    public Brush? MutedBrush { get => (Brush?)GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }
    public Brush? LabelBrush { get => (Brush?)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public Brush? SelectedLabelBrush { get => (Brush?)GetValue(SelectedLabelBrushProperty); set => SetValue(SelectedLabelBrushProperty, value); }
    public Brush? SelectedBackBrush { get => (Brush?)GetValue(SelectedBackBrushProperty); set => SetValue(SelectedBackBrushProperty, value); }
    public Brush? BaselineBrush { get => (Brush?)GetValue(BaselineBrushProperty); set => SetValue(BaselineBrushProperty, value); }
    public FontFamily? LabelFontFamily { get => (FontFamily?)GetValue(LabelFontFamilyProperty); set => SetValue(LabelFontFamilyProperty, value); }
    public double LabelFontSize { get => (double)GetValue(LabelFontSizeProperty); set => SetValue(LabelFontSizeProperty, value); }

    public event Action<int>? MonthPicked;

    private double ColumnWidth => (ActualWidth - Gap * 11) / 12.0;

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (Months is null || ActualWidth <= 0) return;
        var i = (int)Math.Clamp(e.GetPosition(this).X / (ActualWidth / 12.0), 0, 11);
        MonthPicked?.Invoke(i);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        ChartSupport.HitArea(dc, w, h);
        if (Months is not { Count: 12 } months || w <= 0) return;

        var plotH = Math.Max(1, h - MonthBand);
        var barArea = Math.Max(1, plotH - ValueBand);
        var col = ColumnWidth;
        var peak = Math.Max(3600.0, Math.Max(months.Max(m => (double)Math.Max(m.Seconds, m.ProjectedSeconds)), AverageSeconds ?? 0));
        double Px(long seconds) => seconds / peak * barArea;

        FormattedText Text(string s, Brush? brush, bool bold) =>
            ChartSupport.Text(this, s, LabelFontFamily, LabelFontSize, brush, bold ? FontWeights.Bold : FontWeights.Normal);

        var dash = new Pen(ProjectedBrush ?? MutedBrush, 1) { DashStyle = new DashStyle(new double[] { 3, 2 }, 0) };
        var hatch = new Pen(ProjectedBrush ?? MutedBrush, 1) { };
        var dotted = new Pen(MutedBrush, 1) { DashStyle = new DashStyle(new double[] { 1, 2 }, 0) };

        for (var i = 0; i < 12; i++)
        {
            var m = months[i];
            var x = i * (col + Gap);
            var selected = i == SelectedIndex;
            if (selected && SelectedBackBrush is { } back)
                dc.DrawRoundedRectangle(back, null, new Rect(x, 0, col, plotH), 3, 3);

            var baseY = plotH;
            var actualH = Px(m.Seconds);
            var extraH = m.Kind switch
            {
                YearMonthKind.Current => Px(Math.Max(0, m.ProjectedSeconds - m.Seconds)),
                YearMonthKind.Future => Px(m.ProjectedSeconds),
                _ => 0,
            };
            var pad = Math.Min(4, col / 4);
            var bx = x + pad;
            var bw = Math.Max(1, col - pad * 2);

            if (m.Kind == YearMonthKind.BeforeTracking)
                dc.DrawLine(dotted, new Point(bx, baseY - 1), new Point(bx + bw, baseY - 1));

            if (actualH > 0.5)
                dc.DrawRoundedRectangle(m.Kind == YearMonthKind.Current ? CurrentBrush : BarBrush, null,
                    new Rect(bx, baseY - actualH, bw, actualH), m.Kind == YearMonthKind.Current && extraH > 0.5 ? 0 : 2, 2);
            if (extraH > 0.5)
            {
                var rect = new Rect(bx, baseY - actualH - extraH, bw, extraH);
                dc.PushClip(new RectangleGeometry(rect));
                for (var y = rect.Top - bw; y < rect.Bottom; y += 5)
                    dc.DrawLine(hatch, new Point(bx, y + bw), new Point(bx + bw, y));
                dc.Pop();
                // Dashed outline on the open sides only (no bottom edge for the extension).
                dc.DrawLine(dash, new Point(bx, rect.Bottom), new Point(bx, rect.Top));
                dc.DrawLine(dash, new Point(bx, rect.Top), new Point(bx + bw, rect.Top));
                dc.DrawLine(dash, new Point(bx + bw, rect.Top), new Point(bx + bw, rect.Bottom));
            }

            // Value label sits on top of the stack.
            var label = ValueLabels is { } vl && i < vl.Count ? vl[i] : "";
            if (label.Length > 0)
            {
                var t = Text(label, m.Kind == YearMonthKind.BeforeTracking ? MutedBrush : LabelBrush, false);
                var top = baseY - actualH - extraH - t.Height - 1;
                dc.DrawText(t, new Point(x + (col - t.Width) / 2, Math.Max(0, top)));
            }

            // Month name.
            var name = MonthLabels is { } ml && i < ml.Count ? ml[i] : "";
            var nt = Text(name, selected ? SelectedLabelBrush : MutedBrush, selected);
            dc.DrawText(nt, new Point(x + (col - nt.Width) / 2, plotH + (MonthBand - nt.Height) / 2 + 1));
        }

        dc.DrawRectangle(BaselineBrush, null, new Rect(0, plotH, w, 1));

        if (AverageSeconds is { } avg && avg > 0)
        {
            var y = plotH - Px(avg);
            dc.DrawLine(new Pen(MutedBrush, 1) { DashStyle = new DashStyle(new double[] { 4, 3 }, 0) }, new Point(0, y), new Point(w, y));
        }
    }
}
