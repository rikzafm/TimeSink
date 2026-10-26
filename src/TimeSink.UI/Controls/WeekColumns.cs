using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TimeSink.Core.Usage;

namespace TimeSink.UI.Controls;

/// <summary>
/// The Week tab's seven columns: height proportional to the day's time (scaled to the busiest day), the current day in
/// the accent colour, days still to come as a short empty stub. Each column carries its time above and its weekday name
/// below, and a dashed line marks the average of the days that have time. Plain primitives; colours are DPs bound to theme brushes.
/// </summary>
public sealed class WeekColumns : FrameworkElement
{
    private const double Gap = 6;
    private const double LabelH = 14;
    private const double LetterH = 18;
    private const double FontSize = 10;

    private static DependencyProperty Reg<T>(string name, T def = default!) =>
        DependencyProperty.Register(name, typeof(T), typeof(WeekColumns),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DaysProperty = Reg<IReadOnlyList<WeekDay>?>(nameof(Days));
    public static readonly DependencyProperty ColumnBrushProperty = Reg<Brush?>(nameof(ColumnBrush));
    public static readonly DependencyProperty CurrentBrushProperty = Reg<Brush?>(nameof(CurrentBrush));
    public static readonly DependencyProperty FutureBrushProperty = Reg<Brush?>(nameof(FutureBrush));

    public static readonly DependencyProperty LabelBrushProperty = Reg<Brush?>(nameof(LabelBrush));
    public static readonly DependencyProperty AverageBrushProperty = Reg<Brush?>(nameof(AverageBrush));

    /// <summary>Colour of the time and weekday labels (today's use <see cref="CurrentBrush"/>).</summary>
    public Brush? LabelBrush { get => (Brush?)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    /// <summary>Colour of the dashed average line.</summary>
    public Brush? AverageBrush { get => (Brush?)GetValue(AverageBrushProperty); set => SetValue(AverageBrushProperty, value); }

    public static readonly DependencyProperty SelectedBrushProperty = Reg<Brush?>(nameof(SelectedBrush));

    /// <summary>The outline colour of the selected column.</summary>
    public Brush? SelectedBrush { get => (Brush?)GetValue(SelectedBrushProperty); set => SetValue(SelectedBrushProperty, value); }

    public static readonly DependencyProperty SelectedIndexProperty = Reg<int>(nameof(SelectedIndex), -1);

    /// <summary>The column outlined as selected, or -1.</summary>
    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty); set => SetValue(SelectedIndexProperty, value); }

    /// <summary>The pointer is over a column (-1 when it left); the second value is its position in this control.</summary>
    public event Action<int, Point>? ColumnHovered;

    /// <summary>A column was clicked (future days are not clickable).</summary>
    public event Action<int>? ColumnClicked;

    private int IndexAt(Point p)
    {
        if (Days is not { Count: > 0 } days || ActualWidth <= 0 || p.X < 0 || p.X >= ActualWidth) return -1;
        var i = Math.Clamp((int)(p.X / ActualWidth * days.Count), 0, days.Count - 1);
        return days[i].IsFuture ? -1 : i;
    }

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        ColumnHovered?.Invoke(IndexAt(p), p);
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        ColumnHovered?.Invoke(-1, default);
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var i = IndexAt(e.GetPosition(this));
        if (i >= 0) ColumnClicked?.Invoke(i);
    }

    public IReadOnlyList<WeekDay>? Days { get => (IReadOnlyList<WeekDay>?)GetValue(DaysProperty); set => SetValue(DaysProperty, value); }
    public Brush? ColumnBrush { get => (Brush?)GetValue(ColumnBrushProperty); set => SetValue(ColumnBrushProperty, value); }
    public Brush? CurrentBrush { get => (Brush?)GetValue(CurrentBrushProperty); set => SetValue(CurrentBrushProperty, value); }
    public Brush? FutureBrush { get => (Brush?)GetValue(FutureBrushProperty); set => SetValue(FutureBrushProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        ChartSupport.HitArea(dc, w, h);
        if (Days is not { Count: > 0 } days || w <= 0) return;
        var max = Math.Max(1, days.Max(d => d.Seconds));
        var colW = (w - Gap * (days.Count - 1)) / days.Count;
        var plotH = Math.Max(1, h - LabelH - LetterH);
        var baseY = h - LetterH;
        var numbers = TryFindResource("FontFamily.Numbers") as FontFamily;
        var counted = days.Where(d => !d.IsFuture && d.Seconds > 0).ToList();
        if (counted.Count > 0 && AverageBrush is { } avg)
        {
            var y = baseY - counted.Average(d => d.Seconds) / max * plotH;
            dc.DrawLine(new Pen(avg, 1) { DashStyle = new DashStyle([3, 3], 0) }, new Point(0, y), new Point(w, y));
        }
        for (var i = 0; i < days.Count; i++)
        {
            var d = days[i];
            var x = i * (colW + Gap);
            var barH = d.IsFuture ? 6 : Math.Max(6, d.Seconds / (double)max * plotH);
            var brush = d.IsFuture ? FutureBrush : d.IsToday ? CurrentBrush : ColumnBrush;
            dc.DrawRoundedRectangle(brush, null, new Rect(x, baseY - barH, colW, barH), 3, 3);
            if (i == SelectedIndex && SelectedBrush is { } sel)
                dc.DrawRoundedRectangle(null, new Pen(sel, 1.5), new Rect(x - 1.5, baseY - barH - 1.5, colW + 3, barH + 3), 4, 4);

            var text = d.IsToday ? CurrentBrush : LabelBrush;
            if (!d.IsFuture && d.Seconds > 0)
            {
                var value = ChartSupport.Text(this, UsageText.HoursMinutes(d.Seconds), numbers, FontSize, text, FontWeights.Normal);
                dc.DrawText(value, new Point(x + (colW - value.Width) / 2, baseY - barH - value.Height - 2));
            }
            var day = ChartSupport.Text(this, CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(d.Date.DayOfWeek), null, FontSize, text,
                d.IsToday ? FontWeights.SemiBold : FontWeights.Normal);
            dc.DrawText(day, new Point(x + (colW - day.Width) / 2, h - LetterH + 5));
        }
    }
}
