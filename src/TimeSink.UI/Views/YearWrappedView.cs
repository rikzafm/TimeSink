using System.Windows;
using System.Windows.Controls;
using TimeSink.Core.Theming;

namespace TimeSink.UI.Views;

/// <summary>Year > Wrapped. Placeholder until ticket 13 replaces this file's body.</summary>
public sealed class YearWrappedView : UserControl, IYearSubView
{
    public YearWrappedView(string dbPath, TimeSink.UI.Theming.ThemeService themes, TimeSink.UI.Services.LiveState live)
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
