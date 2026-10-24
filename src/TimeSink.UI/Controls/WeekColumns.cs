using System.Windows;
using System.Windows.Media;
using TimeSink.Core.Usage;

namespace TimeSink.UI.Controls;

/// <summary>
/// The Week tab's seven columns: height proportional to the day's time (scaled to the busiest day), the current day in
/// the accent colour, days still to come as a short empty stub. Plain primitives; colours are DPs bound to theme brushes.
/// </summary>
public sealed class WeekColumns : FrameworkElement
{
    private const double Gap = 6;

    private static DependencyProperty Reg<T>(string name, T def = default!) =>
        DependencyProperty.Register(name, typeof(T), typeof(WeekColumns),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty DaysProperty = Reg<IReadOnlyList<WeekDay>?>(nameof(Days));
    public static readonly DependencyProperty ColumnBrushProperty = Reg<Brush?>(nameof(ColumnBrush));
    public static readonly DependencyProperty CurrentBrushProperty = Reg<Brush?>(nameof(CurrentBrush));
    public static readonly DependencyProperty FutureBrushProperty = Reg<Brush?>(nameof(FutureBrush));

    public IReadOnlyList<WeekDay>? Days { get => (IReadOnlyList<WeekDay>?)GetValue(DaysProperty); set => SetValue(DaysProperty, value); }
    public Brush? ColumnBrush { get => (Brush?)GetValue(ColumnBrushProperty); set => SetValue(ColumnBrushProperty, value); }
    public Brush? CurrentBrush { get => (Brush?)GetValue(CurrentBrushProperty); set => SetValue(CurrentBrushProperty, value); }
    public Brush? FutureBrush { get => (Brush?)GetValue(FutureBrushProperty); set => SetValue(FutureBrushProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
        if (Days is not { Count: > 0 } days || w <= 0) return;
        var max = Math.Max(1, days.Max(d => d.Seconds));
        var colW = (w - Gap * (days.Count - 1)) / days.Count;
        for (var i = 0; i < days.Count; i++)
        {
            var d = days[i];
            var x = i * (colW + Gap);
            var barH = d.IsFuture ? 6 : Math.Max(6, d.Seconds / (double)max * h);
            var brush = d.IsFuture ? FutureBrush : d.IsToday ? CurrentBrush : ColumnBrush;
            dc.DrawRoundedRectangle(brush, null, new Rect(x, h - barH, colW, barH), 3, 3);
        }
    }
}
