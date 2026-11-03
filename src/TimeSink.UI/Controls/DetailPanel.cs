using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TimeSink.Core.Usage;

namespace TimeSink.UI.Controls;

public readonly record struct DeviceBarInfo(string Name, long UptimeSeconds, long SleepSeconds, bool IsLocal)
{
    public DeviceBarInfo(string name, long uptimeSeconds, bool isLocal) : this(name, uptimeSeconds, 0, isLocal) { }
    public static implicit operator DeviceBarInfo((string Name, long UptimeSeconds, bool IsLocal) d) => new(d.Name, d.UptimeSeconds, 0, d.IsLocal);
    public static implicit operator DeviceBarInfo((string Name, long UptimeSeconds, long SleepSeconds, bool IsLocal) d) => new(d.Name, d.UptimeSeconds, d.SleepSeconds, d.IsLocal);
}

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
        Padding = new Thickness(14, 12, 14, 12);
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
        ShowCard(title, subtitle,
            apps.Select(a => (a.Name, a.Seconds, a.Palette, 0L)).ToList(),
            compares,
            sessionCount: 0,
            devices: null,
            onBothSeconds: 0,
            summaryPhrase: null,
            actionText: actionText,
            action: action,
            onAppClicked: null);
    }

    public void ShowHourCard(
        string title,
        int sessionCount,
        string subtitle,
        IReadOnlyList<(string Name, long Seconds, int Palette)> apps,
        string summaryPhrase,
        IReadOnlyList<(string Label, string Value, bool Good)> compares)
    {
        ShowCard(title, subtitle,
            apps.Select(a => (a.Name, a.Seconds, a.Palette, 0L)).ToList(),
            compares,
            sessionCount: sessionCount,
            devices: null,
            onBothSeconds: 0,
            summaryPhrase: summaryPhrase,
            actionText: null,
            action: null,
            onAppClicked: null);
    }

    public void ShowHourCard(
        string title,
        int sessionCount,
        string subtitle,
        IReadOnlyList<(string Name, long Seconds, int Palette, long AppId)> apps,
        string summaryPhrase,
        IReadOnlyList<(string Label, string Value, bool Good)> compares,
        IReadOnlyList<DeviceBarInfo>? devices = null,
        long onBothSeconds = 0,
        Action<long>? onAppClicked = null)
    {
        ShowCard(title, subtitle, apps, compares, sessionCount, devices, onBothSeconds, summaryPhrase, actionText: null, action: null, onAppClicked: onAppClicked);
    }

    public void ShowCard(
        string title,
        string subtitle,
        IReadOnlyList<(string Name, long Seconds, int Palette, long AppId)> apps,
        IReadOnlyList<(string Label, string Value, bool Good)>? compares,
        int sessionCount = 0,
        IReadOnlyList<DeviceBarInfo>? devices = null,
        long onBothSeconds = 0,
        string? summaryPhrase = null,
        string? actionText = null,
        Action? action = null,
        Action<long>? onAppClicked = null)
    {
        _body.Children.Clear();
        Padding = new Thickness(14, 12, 14, 12);
        Visibility = Visibility.Visible;

        // Header: Title on left, session count badge & close button on right
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

        var rightStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
        if (sessionCount > 0)
        {
            var badgeText = Text($"{sessionCount} {(sessionCount == 1 ? "session" : "sessions")}", "Brush.TextMuted", "Double.FontSize.Small");
            badgeText.VerticalAlignment = VerticalAlignment.Center;
            badgeText.Margin = new Thickness(0, 0, 10, 0);
            rightStack.Children.Add(badgeText);
        }

        var close = new Button { Content = "✕", Style = (Style)Application.Current.FindResource("LinkButton"), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
        close.Click += (_, _) => { Hide(); CloseRequested?.Invoke(); };
        rightStack.Children.Add(close);

        Grid.SetColumn(rightStack, 1);
        head.Children.Add(rightStack);
        _body.Children.Add(head);

        // Dedicated device breakdown row
        if (devices != null && devices.Count > 0 && (devices.Count > 1 || onBothSeconds > 0 || devices.Any(d => d.SleepSeconds >= 60)))
        {
            var devRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var (devName, devUptime, devSleep, isLocal) in devices)
            {
                if (devUptime <= 0 && devSleep < 60) continue;
                var devItem = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
                var marker = new DeviceMarker { IsLocal = isLocal, Width = 7, Height = 7, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center };
                devItem.Children.Add(marker);

                string statusText;
                if (devUptime > 0 && devSleep >= 60)
                {
                    statusText = $"{devName} {UsageText.HoursMinutes(devUptime)} · asleep {UsageText.HoursMinutes(devSleep)}";
                }
                else if (devSleep >= 60)
                {
                    statusText = $"{devName} asleep {UsageText.HoursMinutes(devSleep)}";
                }
                else
                {
                    statusText = $"{devName} {UsageText.HoursMinutes(devUptime)}";
                }

                var txt = new TextBlock
                {
                    Text = statusText,
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 11,
                };
                txt.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSoft");
                devItem.Children.Add(txt);
                devRow.Children.Add(devItem);
            }
            if (onBothSeconds > 0)
            {
                var bothTxt = new TextBlock
                {
                    Text = $"{UsageText.HoursMinutes(onBothSeconds)} on both",
                    VerticalAlignment = VerticalAlignment.Center,
                    FontSize = 11,
                };
                bothTxt.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
                devRow.Children.Add(bothTxt);
            }
            if (devRow.Children.Count > 0)
            {
                _body.Children.Add(devRow);
            }
        }

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
                var (name, seconds, palette, appId) = apps[i];
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

                if (onAppClicked != null && appId > 0)
                {
                    item.Cursor = Cursors.Hand;
                    item.ToolTip = $"Inspect {name}";
                    var capturedId = appId;
                    item.MouseLeftButtonUp += (_, _) => onAppClicked(capturedId);
                }

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
            summary.Margin = new Thickness(0, 8, 0, 10);
            _body.Children.Add(summary);
        }

        // Comparison metrics
        if (compares != null && compares.Count > 0)
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

        // Action button (e.g. Open in Today)
        if (actionText is not null && action is not null)
        {
            var go = new Button
            {
                Content = actionText,
                Style = (Style)Application.Current.FindResource("LinkButton"),
                Margin = new Thickness(0, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Hand
            };
            go.SetResourceReference(Control.ForegroundProperty, "Brush.Accent");
            go.Click += (_, _) => action();
            _body.Children.Add(go);
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
