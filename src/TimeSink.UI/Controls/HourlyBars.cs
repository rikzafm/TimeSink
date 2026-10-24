using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TimeSink.Core.Usage;

namespace TimeSink.UI.Controls;

/// <summary>
/// Today's hourly chart: 24 columns (height = app-hours open, scale max 6), the current hour in the accent colour,
/// dotted typical-day marks for hours still to come, hatched sleep bands and a "now" marker with a time label.
/// Drawn with plain primitives; every colour is a DP bound to a theme brush.
/// </summary>
public sealed class HourlyBars : FrameworkElement
{
    private const double MaxAppHours = 6, LabelBand = 22, Gap = 3;

    private static DependencyProperty Reg<T>(string name, T def = default!) =>
        DependencyProperty.Register(name, typeof(T), typeof(HourlyBars),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HoursProperty = Reg<IReadOnlyList<HourBucket>?>(nameof(Hours));
    public static readonly DependencyProperty SleepFractionsProperty = Reg<IReadOnlyList<(double Start, double End)>?>(nameof(SleepFractions));
    public static readonly DependencyProperty NowFractionProperty = Reg<double>(nameof(NowFraction));
    public static readonly DependencyProperty NowLabelProperty = Reg<string>(nameof(NowLabel), "");
    public static readonly DependencyProperty CurrentHourProperty = Reg<int>(nameof(CurrentHour), -1);
    public static readonly DependencyProperty BarBrushProperty = Reg<Brush?>(nameof(BarBrush));
    public static readonly DependencyProperty CurrentBrushProperty = Reg<Brush?>(nameof(CurrentBrush));
    public static readonly DependencyProperty MutedBrushProperty = Reg<Brush?>(nameof(MutedBrush));
    public static readonly DependencyProperty LabelTextBrushProperty = Reg<Brush?>(nameof(LabelTextBrush));
    public static readonly DependencyProperty LabelFontFamilyProperty = Reg<FontFamily?>(nameof(LabelFontFamily));
    public static readonly DependencyProperty LabelFontSizeProperty = Reg<double>(nameof(LabelFontSize), 11.0);

    public IReadOnlyList<HourBucket>? Hours { get => (IReadOnlyList<HourBucket>?)GetValue(HoursProperty); set => SetValue(HoursProperty, value); }
    public IReadOnlyList<(double Start, double End)>? SleepFractions { get => ((IReadOnlyList<(double, double)>?)GetValue(SleepFractionsProperty)); set => SetValue(SleepFractionsProperty, value); }
    public double NowFraction { get => (double)GetValue(NowFractionProperty); set => SetValue(NowFractionProperty, value); }
    public string NowLabel { get => (string)GetValue(NowLabelProperty); set => SetValue(NowLabelProperty, value); }
    public int CurrentHour { get => (int)GetValue(CurrentHourProperty); set => SetValue(CurrentHourProperty, value); }
    public Brush? BarBrush { get => (Brush?)GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
    public Brush? CurrentBrush { get => (Brush?)GetValue(CurrentBrushProperty); set => SetValue(CurrentBrushProperty, value); }
    public Brush? MutedBrush { get => (Brush?)GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }
    public Brush? LabelTextBrush { get => (Brush?)GetValue(LabelTextBrushProperty); set => SetValue(LabelTextBrushProperty, value); }
    public FontFamily? LabelFontFamily { get => (FontFamily?)GetValue(LabelFontFamilyProperty); set => SetValue(LabelFontFamilyProperty, value); }
    public double LabelFontSize { get => (double)GetValue(LabelFontSizeProperty); set => SetValue(LabelFontSizeProperty, value); }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        ChartSupport.HitArea(dc, w, h);
        if (Hours is not { Count: 24 } hours || w <= 0) return;

        var plotTop = LabelBand;
        var plotH = Math.Max(1, h - plotTop);
        var col = w / 24.0;
        var barW = Math.Max(1, col - Gap);

        // Sleep bands first so bars draw over them.
        if (SleepFractions is { } bands && MutedBrush is { } muted)
        {
            var pen = new Pen(muted, 1) { };
            foreach (var (s, e) in bands)
            {
                var x0 = Math.Clamp(s, 0, 1) * w;
                var x1 = Math.Clamp(e, 0, 1) * w;
                if (x1 - x0 < 1) continue;
                var rect = new Rect(x0, plotTop, x1 - x0, plotH);
                dc.PushClip(new RectangleGeometry(rect));
                for (var x = x0 - plotH; x < x1; x += 5)
                    dc.DrawLine(pen, new Point(x, plotTop + plotH), new Point(x + plotH, plotTop));
                dc.Pop();
            }
        }

        for (var i = 0; i < 24; i++)
        {
            var x = i * col + Gap / 2;
            var bucket = hours[i];
            var barH = Math.Min(1.0, bucket.AppSeconds / 3600.0 / MaxAppHours) * plotH;
            var future = CurrentHour >= 0 && i > CurrentHour;
            if (barH > 0.5)
                dc.DrawRoundedRectangle(i == CurrentHour ? CurrentBrush : BarBrush, null,
                    new Rect(x, plotTop + plotH - barH, barW, barH), 1.5, 1.5);
            if (future && bucket.TypicalAppSeconds > 0 && MutedBrush is { } m)
            {
                var ty = plotTop + plotH - Math.Min(1.0, bucket.TypicalAppSeconds / 3600.0 / MaxAppHours) * plotH;
                dc.DrawRectangle(m, null, new Rect(x, ty - 1, barW, 2));
            }
        }

        // Now marker + label.
        if (NowFraction is > 0 and <= 1 && CurrentBrush is { } accent)
        {
            var nx = NowFraction * w;
            dc.DrawRectangle(accent, null, new Rect(nx - 0.5, LabelBand - 4, 1, plotH + 4));
            var text = ChartSupport.Text(this, NowLabel, LabelFontFamily, LabelFontSize, LabelTextBrush, FontWeights.SemiBold);
            var pw = text.Width + 12;
            var px = Math.Clamp(nx - pw / 2, 0, w - pw);
            dc.DrawRoundedRectangle(accent, null, new Rect(px, 0, pw, LabelBand - 5), 4, 4);
            dc.DrawText(text, new Point(px + 6, (LabelBand - 5 - text.Height) / 2));
        }
    }
}
