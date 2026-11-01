using System.Windows;
using System.Windows.Controls;
using TimeSink.Core.Devices;

namespace TimeSink.UI.Controls;

/// <summary>
/// Renders a device marker with the theme color and the platform shape rule:
/// Circle for local device (Platform A), Square for remote (Platform B).
/// </summary>
public static class DeviceMarker
{
    public static FrameworkElement Create(bool isLocal, double size = 8.0)
    {
        var border = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(isLocal ? size / 2.0 : 2.0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        border.SetResourceReference(Border.BackgroundProperty, DeviceTokens.BrushKeyFor(isLocal));
        return border;
    }
}
