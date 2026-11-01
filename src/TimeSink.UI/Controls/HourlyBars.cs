using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TimeSink.Core.Usage;

namespace TimeSink.UI.Controls;

/// <summary>
/// Today's hourly chart: 24 columns scaled to peak hour, current hour in accent,
/// hatched sleep bands, and a "now" marker with a time label.
/// Follows Mac HourlyChartView visual style.
/// </summary>
public sealed class HourlyBars : FrameworkElement
{
    private const double MaxUptimeSeconds = 3600.0, LabelBand = 16, Gap = 3;
    private const long MinActivitySeconds = 60;
    private static readonly Brush DefaultTypicalBrush = new SolidColorBrush(Color.FromRgb(0x46, 0x51, 0x4a));

    static HourlyBars()
    {
        DefaultTypicalBrush.Freeze();
    }

    private static DependencyProperty Reg<T>(string name, T def = default!) =>
        DependencyProperty.Register(name, typeof(T), typeof(HourlyBars),
            new FrameworkPropertyMetadata(def, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HoursProperty = Reg<IReadOnlyList<HourBucket>?>(nameof(Hours));
    public static readonly DependencyProperty SleepFractionsProperty = Reg<IReadOnlyList<(double Start, double End)>?>(nameof(SleepFractions));
    public static readonly DependencyProperty SleepBandsProperty = Reg<IReadOnlyList<SleepBand>?>(nameof(SleepBands));
    public static readonly DependencyProperty DayStartUtcProperty = Reg<long>(nameof(DayStartUtc));
    public static readonly DependencyProperty HasTypicalProperty = Reg<bool>(nameof(HasTypical));
    public static readonly DependencyProperty NowFractionProperty = Reg<double>(nameof(NowFraction));
    public static readonly DependencyProperty NowLabelProperty = Reg<string>(nameof(NowLabel), "");
    public static readonly DependencyProperty CurrentHourProperty = Reg<int>(nameof(CurrentHour), -1);
    public static readonly DependencyProperty BarBrushProperty = Reg<Brush?>(nameof(BarBrush));
    public static readonly DependencyProperty CurrentBrushProperty = Reg<Brush?>(nameof(CurrentBrush));
    public static readonly DependencyProperty Heat0Property = Reg<Brush?>(nameof(Heat0));
    public static readonly DependencyProperty Heat1Property = Reg<Brush?>(nameof(Heat1));
    public static readonly DependencyProperty Heat2Property = Reg<Brush?>(nameof(Heat2));
    public static readonly DependencyProperty Heat3Property = Reg<Brush?>(nameof(Heat3));
    public static readonly DependencyProperty TypicalBrushProperty = Reg<Brush?>(nameof(TypicalBrush));
    public static readonly DependencyProperty MutedBrushProperty = Reg<Brush?>(nameof(MutedBrush));
    public static readonly DependencyProperty LabelTextBrushProperty = Reg<Brush?>(nameof(LabelTextBrush));
    public static readonly DependencyProperty LabelFontFamilyProperty = Reg<FontFamily?>(nameof(LabelFontFamily));
    public static readonly DependencyProperty LabelFontSizeProperty = Reg<double>(nameof(LabelFontSize), 10.0);
    public static readonly DependencyProperty DeviceScopeProperty = Reg<TimeSink.Core.Devices.DeviceScope>(nameof(DeviceScope), TimeSink.Core.Devices.DeviceScope.This);
    public static readonly DependencyProperty LocalDeviceIdProperty = Reg<string?>(nameof(LocalDeviceId));
    public static readonly DependencyProperty DeviceABrushProperty = Reg<Brush?>(nameof(DeviceABrush));
    public static readonly DependencyProperty DeviceBBrushProperty = Reg<Brush?>(nameof(DeviceBBrush));

    public static readonly DependencyProperty SelectedHourProperty = Reg<int>(nameof(SelectedHour), -1);
    public static readonly DependencyProperty SelectedBrushProperty = Reg<Brush?>(nameof(SelectedBrush));
    public static readonly DependencyProperty HoveredHourProperty = Reg<int>(nameof(HoveredHour), -1);

    /// <summary>The column under the pointer, or -1.</summary>
    public int HoveredHour { get => (int)GetValue(HoveredHourProperty); private set => SetValue(HoveredHourProperty, value); }

    /// <summary>The outline colour of the selected hour.</summary>
    public Brush? SelectedBrush { get => (Brush?)GetValue(SelectedBrushProperty); set => SetValue(SelectedBrushProperty, value); }

    /// <summary>The hour column outlined as selected, or -1.</summary>
    public int SelectedHour { get => (int)GetValue(SelectedHourProperty); set => SetValue(SelectedHourProperty, value); }

    /// <summary>The pointer is over an hour column (-1 when it left the chart); the second value is its position in this control.</summary>
    public event Action<int, Point>? HourHovered;

    /// <summary>An hour column was clicked.</summary>
    public event Action<int>? HourClicked;

    private int HourAt(Point p) => ActualWidth <= 0 || p.X < 0 || p.X >= ActualWidth ? -1 : Math.Clamp((int)(p.X / ActualWidth * 24), 0, 23);

    protected override void OnMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        var h = HourAt(p);
        HoveredHour = h;
        HourHovered?.Invoke(h, p);
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        HoveredHour = -1;
        HourHovered?.Invoke(-1, default);
    }

    protected override void OnMouseLeftButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var h = HourAt(e.GetPosition(this));
        if (h >= 0) HourClicked?.Invoke(h);
    }

    public IReadOnlyList<HourBucket>? Hours { get => (IReadOnlyList<HourBucket>?)GetValue(HoursProperty); set => SetValue(HoursProperty, value); }
    public IReadOnlyList<(double Start, double End)>? SleepFractions { get => ((IReadOnlyList<(double, double)>?)GetValue(SleepFractionsProperty)); set => SetValue(SleepFractionsProperty, value); }
    public IReadOnlyList<SleepBand>? SleepBands { get => (IReadOnlyList<SleepBand>?)GetValue(SleepBandsProperty); set => SetValue(SleepBandsProperty, value); }
    public long DayStartUtc { get => (long)GetValue(DayStartUtcProperty); set => SetValue(DayStartUtcProperty, value); }
    public bool HasTypical { get => (bool)GetValue(HasTypicalProperty); set => SetValue(HasTypicalProperty, value); }
    public double NowFraction { get => (double)GetValue(NowFractionProperty); set => SetValue(NowFractionProperty, value); }
    public string NowLabel { get => (string)GetValue(NowLabelProperty); set => SetValue(NowLabelProperty, value); }
    public int CurrentHour { get => (int)GetValue(CurrentHourProperty); set => SetValue(CurrentHourProperty, value); }
    public Brush? BarBrush { get => (Brush?)GetValue(BarBrushProperty); set => SetValue(BarBrushProperty, value); }
    public Brush? CurrentBrush { get => (Brush?)GetValue(CurrentBrushProperty); set => SetValue(CurrentBrushProperty, value); }
    public Brush? Heat0 { get => (Brush?)GetValue(Heat0Property); set => SetValue(Heat0Property, value); }
    public Brush? Heat1 { get => (Brush?)GetValue(Heat1Property); set => SetValue(Heat1Property, value); }
    public Brush? Heat2 { get => (Brush?)GetValue(Heat2Property); set => SetValue(Heat2Property, value); }
    public Brush? Heat3 { get => (Brush?)GetValue(Heat3Property); set => SetValue(Heat3Property, value); }
    public Brush? TypicalBrush { get => (Brush?)GetValue(TypicalBrushProperty); set => SetValue(TypicalBrushProperty, value); }
    public Brush? MutedBrush { get => (Brush?)GetValue(MutedBrushProperty); set => SetValue(MutedBrushProperty, value); }
    public Brush? LabelTextBrush { get => (Brush?)GetValue(LabelTextBrushProperty); set => SetValue(LabelTextBrushProperty, value); }
    public FontFamily? LabelFontFamily { get => (FontFamily?)GetValue(LabelFontFamilyProperty); set => SetValue(LabelFontFamilyProperty, value); }
    public double LabelFontSize { get => (double)GetValue(LabelFontSizeProperty); set => SetValue(LabelFontSizeProperty, value); }
    public TimeSink.Core.Devices.DeviceScope DeviceScope { get => (TimeSink.Core.Devices.DeviceScope)GetValue(DeviceScopeProperty); set => SetValue(DeviceScopeProperty, value); }
    public string? LocalDeviceId { get => (string?)GetValue(LocalDeviceIdProperty); set => SetValue(LocalDeviceIdProperty, value); }
    public Brush? DeviceABrush { get => (Brush?)GetValue(DeviceABrushProperty); set => SetValue(DeviceABrushProperty, value); }
    public Brush? DeviceBBrush { get => (Brush?)GetValue(DeviceBBrushProperty); set => SetValue(DeviceBBrushProperty, value); }

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

        var scale = MaxUptimeSeconds;

        var nowH = CurrentHour >= 0 ? CurrentHour
            : NowFraction > 0 ? Math.Min(23, (int)(NowFraction * 24)) : 24;

        var h0 = Heat0 ?? (TryFindResource("Brush.Heat0") as Brush);
        var h1 = Heat1 ?? (TryFindResource("Brush.Heat1") as Brush) ?? BarBrush;
        var h2 = Heat2 ?? (TryFindResource("Brush.Heat2") as Brush) ?? BarBrush;
        var h3 = Heat3 ?? (TryFindResource("Brush.Heat3") as Brush) ?? BarBrush;
        var accent = CurrentBrush ?? (TryFindResource("Brush.Accent") as Brush);
        var typical = TypicalBrush ?? DefaultTypicalBrush;

        bool IsHourAsleep(int hour)
        {
            if (SleepBands is { Count: > 0 })
            {
                var start = DayStartUtc + (long)hour * 3600;
                var end = start + 3600;
                return SleepBands.Any(b => Math.Min(b.EndUtc, end) - Math.Max(b.StartUtc, start) >= 1800);
            }
            if (SleepFractions is { Count: > 0 })
            {
                var sHour = hour / 24.0;
                var eHour = (hour + 1) / 24.0;
                return SleepFractions.Any(b => Math.Min(b.End, eHour) - Math.Max(b.Start, sHour) >= 0.5 / 24.0);
            }
            return false;
        }

        Brush GetBarBrush(long seconds, int hour)
        {
            if (hour == nowH) return accent ?? Brushes.LimeGreen;
            var f = (double)seconds / scale;
            if (f <= 0) return h0 ?? Brushes.DarkSlateGray;
            if (f > 0.75) return h3 ?? Brushes.ForestGreen;
            if (f > 0.5) return h2 ?? Brushes.OliveDrab;
            return h1 ?? Brushes.DarkOliveGreen;
        }

        for (var i = 0; i < 24; i++)
        {
            var x = i * col + Gap / 2.0;
            var bucket = hours[i];
            var isFuture = i > nowH;
            var isAsleep = IsHourAsleep(i);

            // Hover highlight
            if (i == HoveredHour && MutedBrush is { } hoverFill)
            {
                dc.PushOpacity(0.18);
                dc.DrawRoundedRectangle(hoverFill, null, new Rect(x - 1, plotTop, barW + 2, plotH), 2, 2);
                dc.Pop();
            }

            if (isFuture)
            {
                // Future hours: no bar outline drawn
            }
            else if (isAsleep)
            {
                var upH = (bucket.UptimeSeconds / scale) * plotH;
                if (upH > 0.5)
                {
                    var upBrush = GetBarBrush(bucket.UptimeSeconds, i);
                    dc.DrawRoundedRectangle(upBrush, null, new Rect(x, plotTop + plotH - upH, barW, upH), 2, 2);
                }

                var asleepH = Math.Max(upH, plotH * 0.7);
                var rect = new Rect(x, plotTop + plotH - asleepH, barW, asleepH);
                dc.PushClip(new RectangleGeometry(rect, 2, 2));

                var hatchBrush = h2 ?? MutedBrush ?? Brushes.Gray;
                var pen = new Pen(hatchBrush, 1.5);
                pen.Freeze();
                var step = (1.5 + 1.5) * 1.414;
                for (var hx = rect.Left - rect.Height; hx < rect.Right; hx += step)
                {
                    dc.DrawLine(pen, new Point(hx, rect.Bottom), new Point(hx + rect.Height, rect.Top));
                }
                dc.Pop();
            }
            else if (DeviceScope == TimeSink.Core.Devices.DeviceScope.All && bucket.ByDevice != null && bucket.ByDevice.Count > 0)
            {
                long secLocal = 0, secRemote = 0;
                if (LocalDeviceId != null)
                {
                    if (bucket.ByDevice.TryGetValue(LocalDeviceId, out var dhA)) secLocal = dhA.UptimeSeconds;
                    foreach (var (devId, dh) in bucket.ByDevice)
                    {
                        if (devId != LocalDeviceId) secRemote += dh.UptimeSeconds;
                    }
                }
                else
                {
                    var first = true;
                    foreach (var dh in bucket.ByDevice.Values)
                    {
                        if (first) { secLocal = dh.UptimeSeconds; first = false; }
                        else secRemote += dh.UptimeSeconds;
                    }
                }
                var hLocal = secLocal >= MinActivitySeconds ? Math.Max(2.0, (secLocal / scale) * plotH) : 0;
                var hRemote = secRemote >= MinActivitySeconds ? Math.Max(2.0, (secRemote / scale) * plotH) : 0;

                if (hLocal > 0 || hRemote > 0)
                {
                    var (back, front) = OverlapBars.Layout(x, barW, hLocal, hRemote);
                    void DrawDeviceBar(OverlapBarGeometry g)
                    {
                        if (g.Height <= 0.5) return;
                        var brush = g.IsDeviceA ? (i == nowH ? accent : h3)
                                                : (DeviceBBrush ?? h2);
                        dc.DrawRoundedRectangle(brush, null, new Rect(g.X, plotTop + plotH - g.Height, g.Width, g.Height), 2, 2);
                    }
                    DrawDeviceBar(back);
                    DrawDeviceBar(front);
                }
                else
                {
                    var upBrush = GetBarBrush(0, i);
                    dc.DrawRoundedRectangle(upBrush, null, new Rect(x, plotTop + plotH - 2.0, barW, 2.0), 2, 2);
                }
            }
            else
            {
                var barH = bucket.UptimeSeconds >= MinActivitySeconds ? Math.Max(2.0, (bucket.UptimeSeconds / scale) * plotH) : 2.0;
                var brush = GetBarBrush(bucket.UptimeSeconds >= MinActivitySeconds ? bucket.UptimeSeconds : 0, i);
                dc.DrawRoundedRectangle(brush, null, new Rect(x, plotTop + plotH - barH, barW, barH), 2, 2);
            }

            // Selection highlight
            if (i == SelectedHour && SelectedBrush is { } sel)
            {
                dc.DrawRoundedRectangle(null, new Pen(sel, 1.5), new Rect(x - 1, plotTop, barW + 2, plotH), 2, 2);
            }
        }

        // Now marker + label (flag)
        if (NowFraction is > 0 and <= 1 && accent is not null)
        {
            var nx = NowFraction * w;
            dc.DrawRectangle(accent, null, new Rect(nx - 0.5, 0, 1, plotTop + plotH));

            var text = ChartSupport.Text(this, NowLabel, LabelFontFamily, LabelFontSize, LabelTextBrush ?? (TryFindResource("Brush.OnAccent") as Brush) ?? Brushes.Black, FontWeights.Bold);
            var pw = text.Width + 12.0;
            var ph = 16.0;
            var px = Math.Clamp(Math.Min(nx - 24.0, w - pw), 0, Math.Max(0, w - pw));
            dc.DrawRoundedRectangle(accent, null, new Rect(px, 0, pw, ph), 4, 4);
            dc.DrawText(text, new Point(px + 6.0, (ph - text.Height) / 2.0));
        }
    }
}
