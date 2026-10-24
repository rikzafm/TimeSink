using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using TimeSink.Core.Settings;
using TimeSink.UI.Theming;
using static TimeSink.UI.Services.TimeText;

namespace TimeSink.UI;

/// <summary>The single grouped Settings page, built entirely from <see cref="SettingsCatalogue"/>.</summary>
public sealed partial class SettingsView : Grid
{
    private readonly ScrollViewer _scroll = new();
    private readonly string _dbPath;
    private readonly ThemeService _themes;
    private readonly Autostart? _autostart;
    private readonly Dictionary<string, TextBlock> _info = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly AgentSampler _sampler = new();
    private string _theme, _accent;
    private bool _compact;

    public SettingsView(ThemeService themes, string dbPath)
    {
        _themes = themes;
        _dbPath = dbPath;
        _theme = SettingsCatalogue.Read(dbPath, SettingsCatalogue.Find("theme"));
        _accent = SettingsCatalogue.Read(dbPath, SettingsCatalogue.Find("accent"));
        _compact = SettingsCatalogue.Read(dbPath, SettingsCatalogue.Find("compact")) == "true";
        _autostart = TryCreateAutostart();

        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        var root = new StackPanel { Margin = new Thickness(16, 8, 16, 16) };
        foreach (var section in SettingsCatalogue.Sections)
        {
            var header = new TextBlock { Text = section.ToUpperInvariant(), Margin = new Thickness(0, 14, 0, 4), FontWeight = FontWeights.SemiBold };
            header.SetResourceReference(TextBlock.StyleProperty, "Caption");
            header.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            root.Children.Add(header);
            foreach (var row in SettingsCatalogue.InSection(section)) root.Children.Add(BuildRow(row));
        }
        _scroll.Content = root;
        Children.Add(_scroll);

        RefreshInfo();
        RefreshLastBackup();
        Loaded += (_, _) => { RefreshInfo(); _timer.Start(); };
        Unloaded += (_, _) => _timer.Stop();
        _timer.Tick += (_, _) => RefreshInfo();
    }

    private static Autostart? TryCreateAutostart()
    {
        var exe = AgentSampler.FindAgentExe();
        return exe is null ? null : new Autostart(new RegistryRunKey(), exe);
    }

    private UIElement BuildRow(SettingRow row)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        var label = new TextBlock { Text = row.Label, TextWrapping = TextWrapping.Wrap };
        label.SetResourceReference(TextBlock.StyleProperty, "Caption");
        label.SetResourceReference(TextBlock.ForegroundProperty, row.Danger ? "Brush.Danger" : "Brush.Text");
        text.Children.Add(label);
        if (row.Description.Length > 0 || row.Id == "backupNow")
        {
            var desc = new TextBlock { Text = row.Description, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 0) };
            desc.SetResourceReference(TextBlock.StyleProperty, "Caption");
            desc.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
            if (row.Id == "backupNow") _backupDesc = desc;
            text.Children.Add(desc);
        }
        grid.Children.Add(text);

        var control = row.Kind switch
        {
            SettingKind.Switch => BuildSwitch(row),
            SettingKind.Segmented => BuildSegmented(row),
            SettingKind.Button => BuildButton(row),
            _ => BuildInfo(row),
        };
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private FrameworkElement BuildSwitch(SettingRow row)
    {
        var on = row.Id == "startup" ? StartupIsOn(row) : SettingsCatalogue.Read(_dbPath, row) == "true";
        var toggle = new ToggleButton { IsChecked = on };
        toggle.SetResourceReference(StyleProperty, "ToggleSwitch");
        toggle.Click += (_, _) =>
        {
            var value = toggle.IsChecked == true;
            if (row.Id == "startup")
            {
                if (_autostart is null) { toggle.IsChecked = false; return; }
                try { _autostart.SetEnabled(value); }
                catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
                { toggle.IsChecked = _autostart.IsEnabled; return; }
            }
            Persist(row, value ? "true" : "false");
            ApplyAppearance(row, value ? "true" : "false");
        };
        return toggle;
    }

    // The Run key is the truth for Start with Windows (the installer may have written it); fall back to the stored flag.
    private bool StartupIsOn(SettingRow row)
    {
        try { if (_autostart is not null) return _autostart.IsEnabled; } catch (Exception ex) when (ex is System.Security.SecurityException or IOException) { }
        return SettingsCatalogue.Read(_dbPath, row) == "true";
    }

    private FrameworkElement BuildSegmented(SettingRow row)
    {
        var current = SettingsCatalogue.Read(_dbPath, row);
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var o in row.OptionList)
        {
            var b = new RadioButton { Content = o.Label, GroupName = row.Id, IsChecked = o.Value == current };
            b.SetResourceReference(StyleProperty, "SegButton");
            b.Checked += (_, _) => { Persist(row, o.Value); ApplyAppearance(row, o.Value); };
            panel.Children.Add(b);
        }
        return panel;
    }

    private FrameworkElement BuildButton(SettingRow row)
    {
        var b = new Button { Content = Str(row.Id switch { "backupNow" => SettingsStrings.BackUp, "restore" => SettingsStrings.ChooseFile, "export" => SettingsStrings.ExportButton, _ => SettingsStrings.DeleteButton }) };
        b.SetResourceReference(StyleProperty, "LinkButton");
        if (row.Danger) b.SetResourceReference(Control.ForegroundProperty, "Brush.Danger");
        switch (row.Id)
        {
            case "backupNow": b.Click += (_, _) => BackUpNow(b); break;
            case "restore": b.Click += (_, _) => ChooseRestoreFile(); break;
            case "export": b.Click += (_, _) => ChooseExport(); break;
            case "deleteHistory": b.Click += (_, _) => ConfirmDelete(); break;
        }
        return b;
    }

    private FrameworkElement BuildInfo(SettingRow row)
    {
        var t = new TextBlock { HorizontalAlignment = HorizontalAlignment.Right };
        t.SetResourceReference(TextBlock.StyleProperty, "Caption");
        t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSoft");
        _info[row.Id] = t;
        return t;
    }

    private void Persist(SettingRow row, string value)
    {
        var path = _dbPath;
        Task.Run(() => SettingsCatalogue.Write(path, row, value));
    }

    private void ApplyAppearance(SettingRow row, string value)
    {
        if (row.Id == "theme") _theme = value;
        else if (row.Id == "accent") _accent = value;
        else if (row.Id == "compact") _compact = value == "true";
        else return;
        _themes.ApplyPreferences(_theme, _accent, _compact);
    }

    private void RefreshInfo()
    {
        if (_info.TryGetValue("resourceUse", out var ru)) ru.Text = _sampler.Sample();
        if (_info.TryGetValue("trackingSince", out var ts))
        {
            var path = _dbPath;
            Task.Run(() => TrackingSummary.Read(path).Describe())
                .ContinueWith(t => ts.Text = t.Result, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}

/// <summary>Reads the Agent process's working set and CPU between samples.</summary>
internal sealed class AgentSampler
{
    private TimeSpan _lastCpu;
    private DateTime _lastWall;
    private int _lastPid;

    public static string? FindAgentExe()
    {
        foreach (var p in Process.GetProcessesByName("TimeSink.Agent"))
            using (p)
            {
                try { if (p.MainModule?.FileName is { } f) return f; }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
        var sibling = Path.Combine(AppContext.BaseDirectory, "TimeSink.Agent.exe");
        return File.Exists(sibling) ? sibling : null;
    }

    public string Sample()
    {
        foreach (var p in Process.GetProcessesByName("TimeSink.Agent"))
            using (p)
            {
                try
                {
                    var now = DateTime.UtcNow;
                    var cpu = p.TotalProcessorTime;
                    double pct = 0;
                    if (_lastPid == p.Id && _lastWall != default)
                        pct = ResourceUse.CpuPercent(cpu - _lastCpu, now - _lastWall, Environment.ProcessorCount);
                    (_lastPid, _lastCpu, _lastWall) = (p.Id, cpu, now);
                    return ResourceUse.Format(p.WorkingSet64, pct);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { }
            }
        _lastWall = default;
        return ResourceUse.Format(null, 0);
    }
}
