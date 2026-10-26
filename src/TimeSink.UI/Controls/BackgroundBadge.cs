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

    /// <summary>Icon-only form for tight rows (the All table): the layers icon, with "Background App" as a tooltip.</summary>
    public static FrameworkElement CreateIcon()
    {
        var path = new System.Windows.Shapes.Path
        {
            Data = System.Windows.Media.Geometry.Parse("M12,4 L20,8.5 L12,13 L4,8.5 Z M4,12.5 L12,17 L20,12.5 M4,16 L12,20.5 L20,16"),
            StrokeThickness = 2, StrokeStartLineCap = System.Windows.Media.PenLineCap.Round, StrokeEndLineCap = System.Windows.Media.PenLineCap.Round,
            StrokeLineJoin = System.Windows.Media.PenLineJoin.Round,
        };
        path.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "Brush.TextMuted");
        var box = new Viewbox { Width = 14, Height = 14, Child = new Canvas { Width = 24, Height = 24, Background = System.Windows.Media.Brushes.Transparent, Children = { path } }, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var tip = new ToolTip();
        tip.SetResourceReference(ContentControl.ContentProperty, "Str.AddBackgroundApp");
        tip.SetResourceReference(Control.BackgroundProperty, "Brush.Panel");
        tip.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        tip.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        tip.SetResourceReference(Control.FontFamilyProperty, "FontFamily.Ui");
        box.ToolTip = tip;
        return box;
    }
}
