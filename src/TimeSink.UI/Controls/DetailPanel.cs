using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using TimeSink.Core.Usage;

namespace TimeSink.UI.Controls;

/// <summary>
/// The panel under a clicked bar (Today hour, Week day): title and subtitle, a close button, a share-of-time bar per app,
/// optional session lines, comparison lines and one optional action link. Built in code; every colour, font and radius is a
/// theme resource.
/// </summary>
public sealed class DetailPanel : Border
{
    private readonly StackPanel _body = new();

    /// <summary>The ✕ was pressed.</summary>
    public event Action? CloseRequested;

    public DetailPanel()
    {
        Padding = new Thickness(12, 9, 12, 10);
        Margin = new Thickness(0, 2, 0, 12);
        Visibility = Visibility.Collapsed;
        SetResourceReference(BackgroundProperty, "Brush.Panel");
        SetResourceReference(BorderBrushProperty, "Brush.Accent");
        SetResourceReference(BorderThicknessProperty, "Thickness.Border");
        SetResourceReference(CornerRadiusProperty, "CornerRadius.Medium");
        Child = _body;
    }

    public void Hide() => Visibility = Visibility.Collapsed;

    public void Show(
        string title, string subtitle,
        IReadOnlyList<(string Name, long Seconds, int Palette)> apps,
        IReadOnlyList<(string Left, string Middle, string Right)> sessions,
        IReadOnlyList<(string Label, string Value, bool Good)> compares,
        string? actionText = null, Action? action = null)
    {
        _body.Children.Clear();

        var head = new Grid();
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel();
        titles.Children.Add(Text(title, "Brush.Text", "Double.FontSize.Title", FontWeights.SemiBold));
        titles.Children.Add(Text(subtitle, "Brush.TextMuted", "Double.FontSize.Small"));
        var close = new Button { Content = "✕", Style = (Style)Application.Current.FindResource("LinkButton"), VerticalAlignment = VerticalAlignment.Top };
        close.Click += (_, _) => { Hide(); CloseRequested?.Invoke(); };
        Grid.SetColumn(close, 1);
        head.Children.Add(titles);
        head.Children.Add(close);
        _body.Children.Add(head);

        var total = Math.Max(1, apps.Sum(a => a.Seconds));
        foreach (var (name, seconds, palette) in apps)
        {
            var brush = "Brush.App" + palette;
            var line = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            line.ColumnDefinitions.Add(new ColumnDefinition());
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var label = Text(name, "Brush.Text", "Double.FontSize.Body", FontWeights.SemiBold);
            label.TextTrimming = TextTrimming.CharacterEllipsis;
            var value = Text($"{UsageText.HoursMinutes(seconds)} · {UsageText.Share(seconds / (double)total)}",
                "Brush.TextSoft", "Double.FontSize.Small");
            value.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
            Grid.SetColumn(value, 1);
            line.Children.Add(label);
            line.Children.Add(value);
            _body.Children.Add(line);

            var track = new Grid { Height = 6, Margin = new Thickness(0, 3, 0, 0) };
            var trackBg = new Border();
            trackBg.SetResourceReference(Border.BackgroundProperty, "Brush.BorderSoft");
            trackBg.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(seconds, GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(0, total - seconds), GridUnitType.Star) });
            var fill = new Border();
            fill.SetResourceReference(Border.BackgroundProperty, brush);
            fill.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");
            Grid.SetColumnSpan(trackBg, 2);
            track.Children.Add(trackBg);
            track.Children.Add(fill);
            _body.Children.Add(track);
        }

        if (sessions.Count > 0)
        {
            _body.Children.Add(Text("Sessions", "Brush.TextMuted", "Double.FontSize.Small", margin: new Thickness(0, 10, 0, 2)));
            foreach (var (left, middle, right) in sessions)
            {
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
                var mid = Text(middle, "Brush.TextMuted", "Double.FontSize.Small");
                var end = Text(right, "Brush.Text", "Double.FontSize.Small", FontWeights.SemiBold);
                end.TextAlignment = TextAlignment.Right;
                Grid.SetColumn(mid, 1);
                Grid.SetColumn(end, 2);
                row.Children.Add(Text(left, "Brush.TextSoft", "Double.FontSize.Small"));
                row.Children.Add(mid);
                row.Children.Add(end);
                _body.Children.Add(row);
            }
        }

        if (compares.Count > 0)
        {
            var sep = new Border { Height = 1, Margin = new Thickness(0, 10, 0, 6) };
            sep.SetResourceReference(Border.BackgroundProperty, "Brush.BorderSoft");
            _body.Children.Add(sep);
            foreach (var (label, value, good) in compares)
            {
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var v = Text(value, good ? "Brush.Accent" : "Brush.Warning", "Double.FontSize.Small", FontWeights.SemiBold);
                Grid.SetColumn(v, 1);
                row.Children.Add(Text(label, "Brush.TextMuted", "Double.FontSize.Small"));
                row.Children.Add(v);
                _body.Children.Add(row);
            }
        }

        if (actionText is not null && action is not null)
        {
            var go = new Button { Content = actionText, Style = (Style)Application.Current.FindResource("LinkButton"), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            go.SetResourceReference(Control.ForegroundProperty, "Brush.Accent");
            go.Click += (_, _) => action();
            _body.Children.Add(go);
        }

        Visibility = Visibility.Visible;
    }

    private static TextBlock Text(string text, string brush, string size, FontWeight? weight = null, Thickness? margin = null)
    {
        var t = new TextBlock { Text = text };
        if (weight is { } w) t.FontWeight = w;
        if (margin is { } m) t.Margin = m;
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        t.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        t.SetResourceReference(TextBlock.FontSizeProperty, size);
        return t;
    }
}
