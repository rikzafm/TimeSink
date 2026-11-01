using System.Windows;
using System.Windows.Controls;
using TimeSink.Core.Devices;

namespace TimeSink.UI.Controls;

/// <summary>
/// Renders a device marker with the theme color and the platform shape rule:
/// Circle for local device (Platform A), Square for remote (Platform B).
/// </summary>
public sealed class DeviceMarker : Border
{
    static DeviceMarker()
    {
        WidthProperty.OverrideMetadata(typeof(DeviceMarker), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((DeviceMarker)d).Update()));
        HeightProperty.OverrideMetadata(typeof(DeviceMarker), new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((DeviceMarker)d).Update()));
    }

    public static readonly DependencyProperty IsLocalProperty = DependencyProperty.Register(
        nameof(IsLocal), typeof(bool), typeof(DeviceMarker), new PropertyMetadata(true, (d, _) => ((DeviceMarker)d).Update()));

    public bool IsLocal
    {
        get => (bool)GetValue(IsLocalProperty);
        set => SetValue(IsLocalProperty, value);
    }

    public DeviceMarker()
    {
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = HorizontalAlignment.Center;
        Loaded += (_, _) => Update();
        Update();
    }

    private void Update()
    {
        var size = Width > 0 ? Width : (Height > 0 ? Height : 8.0);
        CornerRadius = new CornerRadius(IsLocal ? size / 2.0 : 2.0);
        SetResourceReference(BackgroundProperty, DeviceTokens.BrushKeyFor(IsLocal));
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        Update();
    }

    public static FrameworkElement Create(bool isLocal, double size = 8.0)
    {
        var marker = new DeviceMarker
        {
            IsLocal = isLocal,
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(isLocal ? size / 2.0 : 2.0),
        };
        return marker;
    }
}
