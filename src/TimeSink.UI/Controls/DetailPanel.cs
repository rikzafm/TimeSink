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

    public void ShowHourCard(
        string title,
        int sessionCount,
        string subtitle,
        IReadOnlyList<(string Name, long Seconds, int Palette)> apps,
        string summaryPhrase,
        IReadOnlyList<(string Label, string Value, bool Good)> compares)
    {
        _body.Children.Clear();
        Padding = new Thickness(14, 12, 14, 12);
        Visibility = Visibility.Visible;

        // Header: Title on left, session count badge on right
        var head = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var titles = new StackPanel();
        var titleText = Text(title, "Brush.Text", "Double.FontSize.Title", FontWeights.Bold);
        titleText.FontSize = 18;
        titles.Children.Add(titleText);

        var subText = Text(subtitle, "Brush.TextMuted", "Double.FontSize.Small");
        subText.Margin = new Thickness(0, 2, 0, 0);
        titles.Children.Add(subText);

        Grid.SetColumn(titles, 0);
        head.Children.Add(titles);

        if (sessionCount > 0)
        {
            var badgeText = Text($"{sessionCount} {(sessionCount == 1 ? "session" : "sessions")}", "Brush.TextMuted", "Double.FontSize.Small");
            badgeText.VerticalAlignment = VerticalAlignment.Top;
            badgeText.Margin = new Thickness(0, 4, 0, 0);
            Grid.SetColumn(badgeText, 1);
            head.Children.Add(badgeText);
        }

        _body.Children.Add(head);

        // Segmented bar
        if (apps.Count > 0)
        {
            var barContainer = new Border
            {
                Height = 8,
                Margin = new Thickness(0, 4, 0, 12),
                CornerRadius = new CornerRadius(4),
                ClipToBounds = true
            };
            barContainer.SetResourceReference(Border.BackgroundProperty, "Brush.BorderSoft");

            var barGrid = new Grid();
            for (var i = 0; i < apps.Count; i++)
            {
                barGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(Math.Max(1, apps[i].Seconds), GridUnitType.Star)
                });
                var seg = new Border { Margin = new Thickness(i > 0 ? 1 : 0, 0, i < apps.Count - 1 ? 1 : 0, 0) };
                seg.SetResourceReference(Border.BackgroundProperty, "Brush.App" + apps[i].Palette);
                Grid.SetColumn(seg, i);
                barGrid.Children.Add(seg);
            }
            barContainer.Child = barGrid;
            _body.Children.Add(barContainer);

            // 2-column app list
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16, GridUnitType.Pixel) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var rowCount = (apps.Count + 1) / 2;
            for (var r = 0; r < rowCount; r++)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (var i = 0; i < apps.Count; i++)
            {
                var (name, seconds, palette) = apps[i];
                var col = (i % 2) * 2;
                var row = i / 2;

                var item = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                item.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                item.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                item.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width = 8,
                    Height = 8,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, "Brush.App" + palette);

                var appLabel = Text(name, "Brush.Text", "Double.FontSize.Body", FontWeights.SemiBold);
                appLabel.TextTrimming = TextTrimming.CharacterEllipsis;
                appLabel.VerticalAlignment = VerticalAlignment.Center;

                var duration = Text(UsageText.HoursMinutes(seconds), "Brush.Text", "Double.FontSize.Body", FontWeights.Bold);
                duration.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");
                duration.VerticalAlignment = VerticalAlignment.Center;
                duration.Margin = new Thickness(8, 0, 0, 0);

                Grid.SetColumn(dot, 0);
                Grid.SetColumn(appLabel, 1);
                Grid.SetColumn(duration, 2);

                item.Children.Add(dot);
                item.Children.Add(appLabel);
                item.Children.Add(duration);

                Grid.SetColumn(item, col);
                Grid.SetRow(item, row);
                grid.Children.Add(item);
            }
            _body.Children.Add(grid);
        }

        // Summary phrase
        if (!string.IsNullOrWhiteSpace(summaryPhrase))
        {
            var summary = Text(summaryPhrase, "Brush.TextMuted", "Double.FontSize.Small");
            summary.Margin = new Thickness(0, 10, 0, 12);
            _body.Children.Add(summary);
        }

        // Comparison (Typical for this hour)
        if (compares.Count > 0)
        {
            var divider = new Border { Height = 1, Margin = new Thickness(0, 4, 0, 10) };
            divider.SetResourceReference(Border.BackgroundProperty, "Brush.BorderSoft");
            _body.Children.Add(divider);

            foreach (var (label, value, good) in compares)
            {
                var compGrid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                compGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                compGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var compLabel = Text(label, "Brush.TextMuted", "Double.FontSize.Small");
                var compVal = Text(value, good ? "Brush.Accent" : "Brush.Warning", "Double.FontSize.Small", FontWeights.SemiBold);
                compVal.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Numbers");

                Grid.SetColumn(compLabel, 0);
                Grid.SetColumn(compVal, 1);
                compGrid.Children.Add(compLabel);
                compGrid.Children.Add(compVal);

                _body.Children.Add(compGrid);
            }
        }
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
