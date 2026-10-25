using System.Windows;
using System.Windows.Controls;

namespace TimeSink.UI.Controls;

/// <summary>The small "Background" tag shown next to an app that ranks after the others (All table, app detail).</summary>
public static class BackgroundBadge
{
    public static Border Create()
    {
        var label = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        label.SetResourceReference(TextBlock.TextProperty, "Str.AddBackground");
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        label.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        label.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
        var badge = new Border { Child = label, Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(6, 0, 0, 0), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center };
        badge.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        badge.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Tiny");
        return badge;
    }
}
