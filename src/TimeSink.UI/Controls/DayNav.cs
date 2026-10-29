using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TimeSink.Core.Usage;

namespace TimeSink.UI.Controls;

/// <summary>
/// The Today tab's caption line doubling as the day switcher: the caption in its original spot (opens a
/// month picker when clicked), a plain ◂ in the margin to its left and a plain ▸ hugging its end. Days outside [first tracked day, today] are disabled.
/// Colours, fonts and radii come from theme resources.
/// </summary>
public sealed class DayNav : Grid
{
    private readonly TextBlock _prev = Glyph("◂");
    private readonly TextBlock _next = Glyph("▸");
    private readonly TextBlock _caption = new() { Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center };
    private readonly Popup _popup = new() { StaysOpen = false, AllowsTransparency = true, Placement = PlacementMode.Bottom };
    private readonly Border _popupBody = new() { Width = 252, Padding = new Thickness(8) };

    private PeriodNavigator _days = new(PeriodUnit.Day, default, default);
    private PeriodNavigator _months = new(PeriodUnit.Month, default, default);
    private DateOnly _selected, _month;
    private DayOfWeek _weekStart = DayOfWeek.Monday;

    /// <summary>The user picked a day (never earlier than the first tracked day or later than today).</summary>
    public event Action<DateOnly>? DateChosen;

    /// <summary>The caption shown between the arrows; clicking it opens the day picker.</summary>
    public string CaptionText { get => _caption.Text; set => _caption.Text = value; }

    public DayNav()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition());

        // The caption keeps its original spot; ◂ hangs in the page margin to its left, ▸ hugs its end.
        _prev.HorizontalAlignment = HorizontalAlignment.Left;
        _prev.Margin = new Thickness(-15, 0, 0, 0);
        _next.Margin = new Thickness(4, 0, 0, 0);
        _caption.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        _caption.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        _caption.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        _caption.MouseEnter += (_, _) => _caption.TextDecorations = TextDecorations.Underline;
        _caption.MouseLeave += (_, _) => _caption.TextDecorations = null;
        _caption.MouseLeftButtonUp += (_, _) => OpenPicker(_caption);
        SetColumn(_next, 1);
        Children.Add(_caption);
        Children.Add(_next);
        Children.Add(_prev);

        _prev.MouseLeftButtonUp += (_, _) => { if (_days.CanPrev(_selected)) Choose(_days.Step(_selected, -1)); };
        _next.MouseLeftButtonUp += (_, _) => { if (_days.CanNext(_selected)) Choose(_days.Step(_selected, 1)); };
        _popupBody.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        _popupBody.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        _popupBody.SetResourceReference(Border.BorderThicknessProperty, "Thickness.Border");
        _popupBody.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        _popup.Child = _popupBody;
    }

    public void Configure(DateOnly today, DateOnly first, DateOnly selected, DayOfWeek weekStart)
    {
        _weekStart = weekStart;
        _days = new PeriodNavigator(PeriodUnit.Day, first, today);
        _months = new PeriodNavigator(PeriodUnit.Month, first, today);
        _selected = selected;
        SetEnabled(_prev, _days.CanPrev(selected));
        SetEnabled(_next, _days.CanNext(selected));
    }

    private static void SetEnabled(TextBlock glyph, bool on)
    {
        glyph.IsEnabled = on;
        glyph.Opacity = on ? 1 : 0.35;
        glyph.Cursor = on ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
    }

    private static TextBlock Glyph(string text)
    {
        var g = new TextBlock { Text = text, Padding = new Thickness(3, 0, 3, 0), VerticalAlignment = VerticalAlignment.Center, Background = System.Windows.Media.Brushes.Transparent };
        g.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        g.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        g.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Body");
        g.MouseEnter += (_, _) => { if (g.IsEnabled) g.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text"); };
        g.MouseLeave += (_, _) => g.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        return g;
    }

    private void Choose(DateOnly d)
    {
        d = _days.Clamp(d);
        _popup.IsOpen = false;
        if (d != _selected) DateChosen?.Invoke(d);
    }

    private void OpenPicker(UIElement target)
    {
        _month = _months.StartOf(_selected);
        _popup.PlacementTarget = target;
        BuildMonth();
        _popup.IsOpen = true;
    }

    private void BuildMonth()
    {
        var stack = new StackPanel();
        var head = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition());
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var prev = Link("◂");
        var next = Link("▸");
        var title = Themed(new TextBlock
        {
            Text = _month.ToString("MMMM yyyy", CultureInfo.CurrentCulture),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold,
        }, "Brush.Text");
        prev.IsEnabled = _months.CanPrev(_month);
        next.IsEnabled = _months.CanNext(_month);
        prev.Click += (_, _) => { _month = _months.Step(_month, -1); BuildMonth(); };
        next.Click += (_, _) => { _month = _months.Step(_month, 1); BuildMonth(); };
        SetColumn(title, 1);
        SetColumn(next, 2);
        head.Children.Add(prev);
        head.Children.Add(title);
        head.Children.Add(next);
        stack.Children.Add(head);

        var month = MonthGrid.Layout(_month.Year, _month.Month, _weekStart);
        var grid = new UniformGrid { Columns = 7 };
        foreach (var dow in month.Weekdays)
        {
            var name = CultureInfo.CurrentCulture.DateTimeFormat.GetAbbreviatedDayName(dow);
            grid.Children.Add(Themed(new TextBlock
            {
                Text = name.Length > 2 ? name[..2] : name,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2),
            }, "Brush.TextMuted", "Double.FontSize.Small"));
        }
        for (var i = 0; i < month.Lead; i++) grid.Children.Add(new Border());
        for (var n = 1; n <= month.Days; n++)
        {
            var d = month.First.AddDays(n - 1);
            var ok = _days.Contains(d);
            var label = Themed(new TextBlock { Text = n.ToString(CultureInfo.CurrentCulture), HorizontalAlignment = HorizontalAlignment.Center }, "Brush.Text");
            var cell = new Border { Margin = new Thickness(1), Padding = new Thickness(0, 4, 0, 4), Child = label, Opacity = ok ? 1 : 0.3 };
            cell.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");
            if (d == _selected)
            {
                cell.SetResourceReference(Border.BackgroundProperty, "Brush.Accent");
                label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.OnAccent");
            }
            else if (d == _days.Today)
            {
                cell.SetResourceReference(Border.BorderBrushProperty, "Brush.Accent");
                cell.BorderThickness = new Thickness(1);
            }
            if (ok)
            {
                cell.Cursor = System.Windows.Input.Cursors.Hand;
                cell.Background ??= System.Windows.Media.Brushes.Transparent;
                cell.MouseLeftButtonUp += (_, _) => Choose(d);
            }
            grid.Children.Add(cell);
        }
        stack.Children.Add(grid);
        _popupBody.Child = stack;
    }

    private static Button Link(string text) =>
        new() { Content = text, Style = (Style)Application.Current.FindResource("LinkButton") };

    private static TextBlock Themed(TextBlock t, string brush, string size = "Double.FontSize.Body")
    {
        t.SetResourceReference(TextBlock.ForegroundProperty, brush);
        t.SetResourceReference(TextBlock.FontFamilyProperty, "FontFamily.Ui");
        t.SetResourceReference(TextBlock.FontSizeProperty, size);
        return t;
    }
}
