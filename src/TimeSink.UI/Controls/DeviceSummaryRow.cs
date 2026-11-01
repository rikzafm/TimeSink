using System.Windows;
using System.Windows.Controls;
using TimeSink.Core.Theming;
using TimeSink.Core.Usage;
using TimeSink.UI.Services;
using static TimeSink.UI.Services.TimeText;

namespace TimeSink.UI.Controls;

public class DeviceSummaryRow : WrapPanel
{
    private string? _dbPath;
    private Dictionary<string, (string Name, bool IsLocal)> _deviceInfo = new(StringComparer.OrdinalIgnoreCase);

    public DeviceSummaryRow()
    {
        Orientation = Orientation.Horizontal;
        Visibility = Visibility.Collapsed;
    }

    public void LoadDeviceInfo(string dbPath)
    {
        _dbPath = dbPath;
        _deviceInfo = DeviceInfoService.Load(dbPath);
    }

    public void Update(
        IReadOnlyDictionary<string, long>? deviceTotals,
        long onBothSeconds,
        string? localDeviceId,
        bool useHoursOnly = false)
    {
        if (deviceTotals == null || deviceTotals.Count == 0)
        {
            Visibility = Visibility.Collapsed;
            Children.Clear();
            return;
        }

        Visibility = Visibility.Visible;
        if (_dbPath != null) _deviceInfo = DeviceInfoService.Load(_dbPath);
        Children.Clear();

        // Local device first (Device A, circle), then others (Device B, square)
        var ordered = deviceTotals.OrderByDescending(kv => DeviceInfoService.IsLocal(_deviceInfo, kv.Key, localDeviceId)).ToList();

        foreach (var (devId, secs) in ordered)
        {
            var isLocal = DeviceInfoService.IsLocal(_deviceInfo, devId, localDeviceId);
            var devName = DeviceInfoService.GetName(_deviceInfo, devId, isLocal);
            var formattedTime = useHoursOnly ? $"{UsageText.Hours(secs)} h" : UsageText.HoursMinutes(secs);

            var devItem = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
            var marker = new DeviceMarker { IsLocal = isLocal, Width = 7, Height = 7, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
            devItem.Children.Add(marker);

            var txt = new TextBlock
            {
                Text = $"{devName} {formattedTime}",
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
            };
            txt.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            txt.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
            devItem.Children.Add(txt);
            Children.Add(devItem);
        }

        if (onBothSeconds > 0)
        {
            var formattedOnBoth = useHoursOnly ? $"{UsageText.Hours(onBothSeconds)} h" : UsageText.HoursMinutes(onBothSeconds);
            var bothText = new TextBlock
            {
                Text = Str(StringKeys.SummaryOnBoth, formattedOnBoth),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 11,
            };
            bothText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            bothText.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
            Children.Add(bothText);
        }
    }
}

