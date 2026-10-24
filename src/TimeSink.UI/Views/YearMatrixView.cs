using System.Windows;
using System.Windows.Controls;

namespace TimeSink.UI.Views;

/// <summary>Year > App x month matrix. Placeholder until ticket 14 replaces this file's body.</summary>
public sealed class YearMatrixView : UserControl, IYearSubView
{
    public YearMatrixView(string dbPath, TimeSink.UI.Theming.ThemeService themes, TimeSink.UI.Services.LiveState live)
    {
        var text = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        text.SetResourceReference(FrameworkElement.StyleProperty, "Caption");
        text.SetResourceReference(TextBlock.TextProperty, "Str.StubComingSoon");
        Content = text;
    }

    public void Start() { }
    public void Stop() { }
    public void Refresh() { }
}
