using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TimeSink.UI;

public sealed partial class SettingsView
{
    private UIElement? _keyboardGuideOverlay;

    public void OpenKeyboardGuide()
    {
        CloseKeyboardGuide();
        var view = BuildKeyboardGuideView();
        _keyboardGuideOverlay = view;
        Grid.SetRowSpan(view, 2);
        Panel.SetZIndex(view, 5);
        Children.Add(view);
    }

    public bool TryCloseKeyboardGuide()
    {
        if (_keyboardGuideOverlay is null) return false;
        CloseKeyboardGuide();
        return true;
    }

    public void CloseKeyboardGuide()
    {
        if (_keyboardGuideOverlay is null) return;
        Children.Remove(_keyboardGuideOverlay);
        _keyboardGuideOverlay = null;
    }

    private UIElement BuildKeyboardGuideView()
    {
        var root = new Grid();
        root.SetResourceReference(Panel.BackgroundProperty, "Brush.Surface");
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44, GridUnitType.Pixel) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        // Header
        var header = new Grid { Margin = new Thickness(10, 0, 10, 0) };
        var backBtn = new Button
        {
            Content = "‹ Back",
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(8, 4, 8, 4),
        };
        backBtn.SetResourceReference(StyleProperty, "LinkButton");
        backBtn.SetResourceReference(Control.ForegroundProperty, "Brush.Accent");
        backBtn.Click += (_, _) => CloseKeyboardGuide();

        var title = new TextBlock
        {
            Text = "Keyboard shortcuts",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");

        header.Children.Add(backBtn);
        header.Children.Add(title);

        var border = new Border { BorderThickness = new Thickness(0, 0, 0, 1), Margin = new Thickness(0) };
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");
        border.Child = header;
        Grid.SetRow(border, 0);
        root.Children.Add(border);

        // Content
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Focusable = false,
        };

        var stack = new StackPanel { Margin = new Thickness(16, 12, 16, 20) };

        // 1. Navigation & Tabs
        stack.Children.Add(SectionHeader("NAVIGATION & TABS"));
        stack.Children.Add(ShortcutRow("Switch to tab", "Today (1), Week (2), Month (3), Year (4), All (5)", ["Ctrl", "1..5"]));
        stack.Children.Add(ShortcutRow("Next tab", "Cycle forward across tabs", ["Ctrl", "Tab"]));
        stack.Children.Add(ShortcutRow("Previous tab", "Cycle backward across tabs", ["Ctrl", "Shift", "Tab"]));
        stack.Children.Add(ShortcutRow("Date back / forward", "Step days, weeks, months, or years", ["Alt", "← / →"]));

        // 2. Go Back & Exit
        stack.Children.Add(SectionHeader("GO BACK & EXIT"));
        stack.Children.Add(ShortcutRow("Escape (Hierarchical exit)", "1. Clears search/input text\n2. Closes dialogs, sheets & sub-screens\n3. Closes overlays (Settings, Add App, Detail)\n4. Resets historical day to live Today\n5. Dismisses flyout window (tracking stays active)", ["Esc"]));
        stack.Children.Add(ShortcutRow("Secondary go back", "Pops back one view in overlays and drill-downs", ["Alt", "←"], ["Backspace"]));

        // 3. Quick Actions
        stack.Children.Add(SectionHeader("QUICK ACTIONS"));
        stack.Children.Add(ShortcutRow("Add app", "Open the Add App overlay", ["Ctrl", "N"]));
        stack.Children.Add(ShortcutRow("Games filter", "Toggle Games-only view", ["Ctrl", "G"]));
        stack.Children.Add(ShortcutRow("Settings", "Toggle the Settings screen", ["Ctrl", ","]));
        stack.Children.Add(ShortcutRow("Shortcuts guide", "Open this guide from anywhere", ["?"]));

        scroll.Content = stack;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        return root;
    }

    private static UIElement SectionHeader(string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 14, 0, 8),
        };
        tb.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        return tb;
    }

    private static UIElement ShortcutRow(string action, string? subtext, params string[][] badgeGroups)
    {
        var card = new Border
        {
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 6),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
        };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leftStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var label = new TextBlock
        {
            Text = action,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        leftStack.Children.Add(label);

        if (!string.IsNullOrEmpty(subtext))
        {
            var desc = new TextBlock
            {
                Text = subtext,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            desc.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            leftStack.Children.Add(desc);
        }
        Grid.SetColumn(leftStack, 0);
        grid.Children.Add(leftStack);

        var rightStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
        };

        for (int i = 0; i < badgeGroups.Length; i++)
        {
            if (i > 0)
            {
                var orText = new TextBlock
                {
                    Text = "or",
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 4, 0),
                };
                orText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
                rightStack.Children.Add(orText);
            }

            var group = badgeGroups[i];
            for (int j = 0; j < group.Length; j++)
            {
                if (j > 0)
                {
                    var plus = new TextBlock
                    {
                        Text = "+",
                        FontSize = 11,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(2, 0, 2, 0),
                    };
                    plus.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
                    rightStack.Children.Add(plus);
                }
                rightStack.Children.Add(KeyBadge(group[j]));
            }
        }

        Grid.SetColumn(rightStack, 1);
        grid.Children.Add(rightStack);

        card.Child = grid;
        return card;
    }

    private static UIElement KeyBadge(string key)
    {
        var border = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");

        var text = new TextBlock
        {
            Text = key,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        text.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        text.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Monospace");

        border.Child = text;
        return border;
    }
}

