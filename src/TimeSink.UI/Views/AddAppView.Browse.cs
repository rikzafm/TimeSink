using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using TimeSink.Core.Apps;
using TimeSink.UI.Services;

namespace TimeSink.UI.Views;

/// <summary>Browse tab: drop or choose an .exe or shortcut, plus shortcuts to common program folders.</summary>
public sealed partial class AddAppView
{
    private TextBlock _browseStatus = null!;

    private UIElement BuildBrowseTab()
    {
        var host = new StackPanel { Margin = new Thickness(16, 14, 16, 8) };

        // drop zone
        var zone = new Grid { AllowDrop = true, Background = Brushes.Transparent };
        var dashed = new Rectangle { StrokeDashArray = [4, 3], StrokeThickness = 1.5, Fill = Brushes.Transparent };
        dashed.SetResourceReference(Rectangle.RadiusXProperty, "Double.Radius.Large");
        dashed.SetResourceReference(Rectangle.RadiusYProperty, "Double.Radius.Large");
        dashed.SetResourceReference(Shape.StrokeProperty, "Brush.Border");
        var inner = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(12, 22, 12, 22) };
        var arrow = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M12,15 L12,4 M7,9 L12,4 L17,9 M4,15 L4,19 A1,1 0 0 0 5,20 L19,20 A1,1 0 0 0 20,19 L20,15"),
            StrokeThickness = 1.8, Width = 34, Height = 34, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
        };
        arrow.SetResourceReference(Shape.StrokeProperty, "Brush.Accent");
        var title = Text("Str.AddDropTitle", "Double.FontSize.Title", "Brush.Text");
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.FontWeight = FontWeights.SemiBold;
        title.Margin = new Thickness(0, 8, 0, 2);
        var sub = Text("Str.AddDropSub", "Double.FontSize.Small", "Brush.TextMuted");
        sub.HorizontalAlignment = HorizontalAlignment.Center;
        sub.TextAlignment = TextAlignment.Center;
        sub.TextWrapping = TextWrapping.Wrap;
        var choose = Pill("Str.AddChoose", PillStyle.Filled);
        choose.Margin = new Thickness(0, 12, 0, 0);
        choose.HorizontalAlignment = HorizontalAlignment.Center;
        choose.Click += (_, _) => ChooseFile(null);
        inner.Children.Add(arrow);
        inner.Children.Add(title);
        inner.Children.Add(sub);
        inner.Children.Add(choose);
        zone.Children.Add(dashed);
        zone.Children.Add(inner);
        zone.DragOver += (_, e) =>
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        };
        zone.Drop += (_, e) =>
        {
            if (e.Data.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files) AddFromFile(files[0]);
        };
        host.Children.Add(zone);

        _browseStatus = Text(null, "Double.FontSize.Body", "Brush.TextMuted");
        _browseStatus.Margin = new Thickness(4, 10, 4, 0);
        _browseStatus.TextWrapping = TextWrapping.Wrap;
        _browseStatus.Visibility = Visibility.Collapsed;
        host.Children.Add(_browseStatus);

        // common places
        var places = Text("Str.AddPlaces", "Double.FontSize.Small", "Brush.TextMuted");
        places.Margin = new Thickness(4, 18, 0, 6);
        host.Children.Add(places);
        foreach (var place in CommonPlaces.All()) host.Children.Add(PlaceRow(place));

        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, Content = host };
    }

    private UIElement PlaceRow(CommonPlace place)
    {
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var folder = Glyph("M3,7 A2,2 0 0 1 5,5 L9,5 L11,7 L19,7 A2,2 0 0 1 21,9 L21,17 A2,2 0 0 1 19,19 L5,19 A2,2 0 0 1 3,17 Z", 18, "Brush.TextSoft");
        folder.Margin = new Thickness(10, 0, 12, 0);
        var texts = new StackPanel { Margin = new Thickness(0, 9, 0, 9) };
        var name = Text(null, "Double.FontSize.Body", place.Exists ? "Brush.Text" : "Brush.TextMuted");
        name.Text = place.Name;
        name.FontWeight = FontWeights.SemiBold;
        var path = Text(null, "Double.FontSize.Small", "Brush.TextMuted");
        path.Text = place.Path;
        path.TextTrimming = TextTrimming.CharacterEllipsis;
        texts.Children.Add(name);
        texts.Children.Add(path);
        var chevron = Glyph("M9,6 L15,12 L9,18", 14, "Brush.TextMuted");
        chevron.Margin = new Thickness(8, 0, 10, 0);
        Grid.SetColumn(texts, 1);
        Grid.SetColumn(chevron, 2);
        g.Children.Add(folder);
        g.Children.Add(texts);
        g.Children.Add(chevron);

        var card = new Border { BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 6), Child = g, Cursor = System.Windows.Input.Cursors.Hand };
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");
        card.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        card.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");
        card.MouseLeftButtonUp += (_, _) =>
        {
            if (place.Exists) ChooseFile(place.Path);
            else ShowStatus(TimeText.Str(AddAppStrings.NoFolder), error: true);
        };
        return card;
    }

    private static System.Windows.Shapes.Path Glyph(string data, double size, string brushKey)
    {
        var p = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(data), StrokeThickness = 1.8, Width = size, Height = size, Stretch = Stretch.Uniform,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, VerticalAlignment = VerticalAlignment.Center,
        };
        p.SetResourceReference(Shape.StrokeProperty, brushKey);
        return p;
    }

    private void ChooseFile(string? folder)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "Apps and shortcuts (*.exe;*.lnk)|*.exe;*.lnk",
            InitialDirectory = folder is not null && Directory.Exists(folder) ? folder : "",
        };
        var window = Window.GetWindow(this) as FlyoutWindow;
        if (window is not null) window.KeepOpen = true; // the dialog deactivates the flyout, which would close it
        bool ok;
        try { ok = dlg.ShowDialog(window) == true; }
        finally { if (window is not null) { window.KeepOpen = false; window.Activate(); } }
        if (ok) AddFromFile(dlg.FileName);
    }

    private void AddFromFile(string path)
    {
        var result = BrowseTarget.Resolve(path, InstalledAppsService.ResolveLnk);
        if (result.App is not { } app)
        {
            ShowStatus(TimeText.Str(result.Error == BrowseError.TargetMissing ? AddAppStrings.TargetMissing : AddAppStrings.NotAnApp), error: true);
            return;
        }
        Task.Run(() => IconService.ExtractPng(app.ExePath, null, null, TileIconPx)).ContinueWith(t =>
        {
            try
            {
                using var lib = AppLibrary.Open(_dbPath);
                lib.AddTracked(app, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), t.Result);
            }
            catch { return; }
            _addedElsewhere.Add(app.ExePath);
            UpdateCount();
            ShowStatus(app.DisplayName + " · " + TimeText.Str(AddAppStrings.Tracking), error: false);
        }, TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void ShowStatus(string text, bool error)
    {
        _browseStatus.Text = text;
        _browseStatus.SetResourceReference(TextBlock.ForegroundProperty, error ? "Brush.TextMuted" : "Brush.Accent");
        _browseStatus.Visibility = Visibility.Visible;
    }
}
