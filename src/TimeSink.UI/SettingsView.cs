using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TimeSink.Core.Data;
using TimeSink.Core.Devices;
using TimeSink.Core.Icons;
using TimeSink.Core.Ipc;
using TimeSink.Core.Settings;
using TimeSink.UI.Services;
using TimeSink.UI.Theming;
using TimeSink.UI.Views;
using static TimeSink.UI.Services.TimeText;

namespace TimeSink.UI;

/// <summary>The single grouped Settings page, built entirely from <see cref="SettingsCatalogue"/>.</summary>
public sealed partial class SettingsView : Grid
{
    private readonly ScrollViewer _scroll = new();
    private readonly string _dbPath;
    private readonly ThemeService _themes;
    private readonly Autostart? _autostart;
    private readonly Action? _close;
    private readonly Dictionary<string, TextBlock> _info = new();
    private readonly Dictionary<string, TextBlock> _values = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly AgentSampler _sampler = new();
    private string _theme, _accent;
    private bool _compact;
    private TextBlock? _syncDesc;
    private TextBlock? _syncStatusText;
    private TextBlock? _deviceNameText;
    private Action<string>? _showIcon;

    public SettingsView(ThemeService themes, string dbPath) : this(themes, dbPath, null) { }

    public SettingsView(ThemeService themes, string dbPath, Action? close)
    {
        _themes = themes;
        _dbPath = dbPath;
        _close = close;
        _theme = SettingsCatalogue.Read(dbPath, SettingsCatalogue.Find("theme"));
        _accent = SettingsCatalogue.Read(dbPath, SettingsCatalogue.Find("accent"));
        _compact = SettingsCatalogue.Read(dbPath, SettingsCatalogue.Find("compact")) == "true";
        _autostart = TryCreateAutostart();

        SetResourceReference(Panel.BackgroundProperty, "Brush.Surface");

        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header (44px)
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content

        var header = BuildHeader();
        Grid.SetRow(header, 0);
        Children.Add(header);

        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Hidden;
        _scroll.Focusable = false;

        var content = new StackPanel { Margin = new Thickness(14, 8, 14, 20) };
        foreach (var section in SettingsCatalogue.Sections)
        {
            var rows = SettingsCatalogue.InSection(section).ToList();
            if (rows.Count == 0) continue;
            content.Children.Add(BuildSection(section, rows));
        }

        _scroll.Content = content;
        Grid.SetRow(_scroll, 1);
        Children.Add(_scroll);

        RefreshInfo();
        RefreshLastBackup();
        Loaded += (_, _) => { RefreshInfo(); _timer.Start(); Focus(); };
        Unloaded += (_, _) => _timer.Stop();
        _timer.Tick += (_, _) => RefreshInfo();

        Focusable = true;
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || Keyboard.FocusedElement is TextBox) return;
            e.Handled = true;
            if (TryCloseSubView()) return;
            _close?.Invoke();
        };
    }

    private static Autostart? TryCreateAutostart()
    {
        var exe = AgentSampler.FindAgentExe();
        return exe is null ? null : new Autostart(new RegistryRunKey(), exe);
    }

    private UIElement BuildHeader()
    {
        var header = new Grid { Height = 44, Margin = new Thickness(10, 0, 10, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var backBtn = new Button
        {
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(6, 4, 6, 4),
        };
        backBtn.SetResourceReference(StyleProperty, "LinkButton");

        var backPanel = new StackPanel { Orientation = Orientation.Horizontal };
        var chevron = new TextBlock
        {
            Text = "‹",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, -1, 4, 0),
        };
        chevron.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");

        var backText = new TextBlock
        {
            Text = "Back",
            FontSize = 13,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
        };
        backText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");

        backPanel.Children.Add(chevron);
        backPanel.Children.Add(backText);
        backBtn.Content = backPanel;
        backBtn.Click += (_, _) =>
        {
            if (TryCloseSubView()) return;
            _close?.Invoke();
        };
        Grid.SetColumn(backBtn, 0);
        header.Children.Add(backBtn);

        var title = new TextBlock
        {
            Text = "Settings",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        Grid.SetColumn(title, 0);
        Grid.SetColumnSpan(title, 3);
        header.Children.Add(title);

        var border = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = header,
        };
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");
        return border;
    }

    private UIElement BuildSection(string section, List<SettingRow> rows)
    {
        var sectionPanel = new StackPanel { Margin = new Thickness(0, 6, 0, 8) };

        var header = new TextBlock
        {
            Text = section.ToUpperInvariant(),
            Margin = new Thickness(6, 4, 0, 6),
            FontWeight = FontWeights.SemiBold,
            FontSize = 10,
        };
        header.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        sectionPanel.Children.Add(header);

        var card = new Border
        {
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            ClipToBounds = true,
        };
        card.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
        card.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");

        var rowsPanel = new StackPanel();
        for (int i = 0; i < rows.Count; i++)
        {
            var isFirst = i == 0;
            rowsPanel.Children.Add(BuildCardRow(rows[i], isFirst));
        }

        card.Child = rowsPanel;
        sectionPanel.Children.Add(card);
        return sectionPanel;
    }

    private UIElement BuildCardRow(SettingRow row, bool isFirst)
    {
        var rowBorder = new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            BorderThickness = new Thickness(0, isFirst ? 0 : 1, 0, 0),
        };
        rowBorder.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var textPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
        };

        var label = new TextBlock
        {
            Text = row.Label,
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, row.Danger ? "Brush.Danger" : "Brush.Text");
        textPanel.Children.Add(label);

        if (!string.IsNullOrEmpty(row.Description) || row.Id == "backupNow" || row.Id == "devicesSync")
        {
            var desc = new TextBlock
            {
                Text = row.Description,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            };
            desc.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            if (row.Id == "backupNow") _backupDesc = desc;
            if (row.Id == "devicesSync") _syncDesc = desc;
            textPanel.Children.Add(desc);
        }
        grid.Children.Add(textPanel);

        var control = BuildControl(row);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);

        rowBorder.Child = grid;
        return rowBorder;
    }

    private FrameworkElement BuildControl(SettingRow row)
    {
        return row.Id switch
        {
            "devicesSync" => BuildDevicesSyncControl(row),
            "deviceName" => BuildDeviceNameControl(row),
            _ => row.Kind switch
            {
                SettingKind.Switch => BuildSwitch(row),
                SettingKind.Segmented => BuildPicker(row),
                SettingKind.Button => BuildButton(row),
                SettingKind.Icon => BuildIconRow(row),
                _ => BuildInfo(row),
            }
        };
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

    private bool StartupIsOn(SettingRow row)
    {
        try { if (_autostart is not null) return _autostart.IsEnabled; } catch (Exception ex) when (ex is System.Security.SecurityException or IOException) { }
        return SettingsCatalogue.Read(_dbPath, row) == "true";
    }

    private FrameworkElement BuildPicker(SettingRow row)
    {
        var current = SettingsCatalogue.Read(_dbPath, row);
        var valueText = new TextBlock
        {
            Text = FormatOptionLabel(row, current),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        valueText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        _values[row.Id] = valueText;

        var chevron = new TextBlock
        {
            Text = "›",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        chevron.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(valueText);
        panel.Children.Add(chevron);

        var btn = new Button
        {
            Content = panel,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 2, 4, 2),
        };
        btn.SetResourceReference(StyleProperty, "LinkButton");

        var menu = new StackPanel { Margin = new Thickness(2) };
        var frame = new Border
        {
            BorderThickness = new Thickness(1),
            Child = menu,
            MinWidth = 100,
        };
        frame.SetResourceReference(Border.BackgroundProperty, "Brush.Surface");
        frame.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        frame.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");

        var popup = new Popup
        {
            PlacementTarget = btn,
            Placement = PlacementMode.Custom,
            StaysOpen = false,
            AllowsTransparency = true,
            Child = frame,
        };

        popup.CustomPopupPlacementCallback = (popupSize, targetSize, offset) =>
        {
            var point = new Point(targetSize.Width - popupSize.Width, targetSize.Height + 3);
            return [new CustomPopupPlacement(point, PopupPrimaryAxis.Horizontal)];
        };

        void PopulateMenu()
        {
            menu.Children.Clear();
            var currentVal = SettingsCatalogue.Read(_dbPath, row);
            foreach (var opt in row.OptionList)
            {
                var optVal = opt.Value;
                var isSelected = optVal == currentVal;

                var item = new Border
                {
                    Padding = new Thickness(12, 7, 12, 7),
                    Cursor = Cursors.Hand,
                    Background = Brushes.Transparent,
                };
                item.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");

                var label = new TextBlock
                {
                    Text = FormatOptionLabel(row, optVal),
                    FontSize = 12,
                    FontWeight = isSelected ? FontWeights.SemiBold : FontWeights.Normal,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                label.SetResourceReference(TextBlock.ForegroundProperty, isSelected ? "Brush.Accent" : "Brush.Text");
                item.Child = label;

                item.MouseEnter += (_, _) => item.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
                item.MouseLeave += (_, _) => item.Background = Brushes.Transparent;

                item.MouseLeftButtonUp += (_, _) =>
                {
                    popup.IsOpen = false;
                    Persist(row, optVal);
                    ApplyAppearance(row, optVal);
                    valueText.Text = FormatOptionLabel(row, optVal);
                };

                menu.Children.Add(item);
            }
        }

        popup.Opened += (_, _) =>
        {
            valueText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            chevron.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            chevron.Text = "▾";
            chevron.Margin = new Thickness(4, 1, 0, 0);
        };

        popup.Closed += (_, _) =>
        {
            valueText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            chevron.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            chevron.Text = "›";
            chevron.Margin = new Thickness(4, 0, 0, 0);
        };

        var closedAt = DateTime.MinValue;
        popup.Closed += (_, _) => closedAt = DateTime.UtcNow;

        btn.Click += (_, _) =>
        {
            if (DateTime.UtcNow - closedAt < TimeSpan.FromMilliseconds(200)) return;
            PopulateMenu();
            popup.IsOpen = true;
        };

        var host = new Grid();
        host.Children.Add(btn);
        host.Children.Add(popup);
        return host;
    }

    private static string FormatOptionLabel(SettingRow row, string value)
    {
        if (row.Id == "weekStart")
        {
            return value switch
            {
                "Mon" => "Monday",
                "Sun" => "Sunday",
                "Sat" => "Saturday",
                _ => value
            };
        }
        if (row.Id == "minSession")
        {
            return $"{value.TrimEnd('s')} s";
        }
        return row.OptionList.FirstOrDefault(o => o.Value == value)?.Label ?? value;
    }

    private FrameworkElement BuildDevicesSyncControl(SettingRow row)
    {
        var statusText = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        statusText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        _syncStatusText = statusText;

        var chevron = new TextBlock
        {
            Text = "›",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        chevron.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(statusText);
        panel.Children.Add(chevron);

        var btn = new Button
        {
            Content = panel,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 2, 4, 2),
        };
        btn.SetResourceReference(StyleProperty, "LinkButton");
        btn.Click += (_, _) => OpenDevicesSync();

        UpdateSyncStatus();
        return btn;
    }

    private void UpdateSyncStatus()
    {
        var syncOn = AppSettings.ForFile(_dbPath).Get(Setting.SyncEnabled);
        var syncFolder = AppSettings.ForFile(_dbPath).Get(Setting.SyncFolder);

        if (_syncStatusText is not null)
        {
            _syncStatusText.Text = syncOn ? "On" : "Off";
        }

        if (_syncDesc is not null)
        {
            if (!syncOn)
            {
                _syncDesc.Text = "Off";
            }
            else if (string.IsNullOrWhiteSpace(syncFolder))
            {
                _syncDesc.Text = "No sync folder set";
            }
            else
            {
                var parent = Path.GetFileName(Path.GetDirectoryName(syncFolder.TrimEnd('\\', '/')));
                var folderName = Path.GetFileName(syncFolder.TrimEnd('\\', '/'));
                if (!string.IsNullOrEmpty(parent) && parent.Contains("OneDrive", StringComparison.OrdinalIgnoreCase))
                    _syncDesc.Text = $"OneDrive · {folderName} folder";
                else if (!string.IsNullOrEmpty(parent) && parent.Contains("Google", StringComparison.OrdinalIgnoreCase))
                    _syncDesc.Text = $"Google Drive · {folderName} folder";
                else if (!string.IsNullOrEmpty(folderName))
                    _syncDesc.Text = $"{folderName} folder";
                else
                    _syncDesc.Text = syncFolder;
            }
        }
    }

    private FrameworkElement BuildDeviceNameControl(SettingRow row)
    {
        var nameText = new TextBlock
        {
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        nameText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        _deviceNameText = nameText;

        var chevron = new TextBlock
        {
            Text = "›",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        chevron.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(nameText);
        panel.Children.Add(chevron);

        var btn = new Button
        {
            Content = panel,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 2, 4, 2),
        };
        btn.SetResourceReference(StyleProperty, "LinkButton");
        btn.Click += (_, _) =>
        {
            var localDev = LoadLocalDevice();
            if (localDev is not null) PromptRenameDevice(localDev, RefreshInfo);
        };

        UpdateDeviceName();
        return btn;
    }

    private DeviceRecord? LoadLocalDevice()
    {
        try
        {
            using var db = Db.Open(_dbPath);
            var devInfo = DeviceInfoService.Load(_dbPath);
            var local = devInfo.FirstOrDefault(kv => kv.Value.IsLocal);
            var name = !string.IsNullOrWhiteSpace(local.Value.Name) ? local.Value.Name : Environment.MachineName;
            var id = local.Key ?? "local";
            return new DeviceRecord(id, name, "Windows", true, DateTimeOffset.UtcNow, null, null, "active");
        }
        catch
        {
            return new DeviceRecord("local", Environment.MachineName, "Windows", true, DateTimeOffset.UtcNow, null, null, "active");
        }
    }

    private void UpdateDeviceName()
    {
        if (_deviceNameText is null) return;
        var name = AppSettings.ForFile(_dbPath).Get(Setting.DeviceName);
        if (string.IsNullOrWhiteSpace(name))
        {
            var devInfo = DeviceInfoService.Load(_dbPath);
            name = devInfo.FirstOrDefault(kv => kv.Value.IsLocal).Value.Name;
        }
        _deviceNameText.Text = !string.IsNullOrWhiteSpace(name) ? name : Environment.MachineName;
    }

    private FrameworkElement BuildIconRow(SettingRow row)
    {
        var thumb = new ContentControl { VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            FontSize = 12,
        };
        name.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");

        var chevron = new TextBlock
        {
            Text = "›",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(4, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        chevron.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(thumb);
        panel.Children.Add(name);
        panel.Children.Add(chevron);

        void Show(string id)
        {
            thumb.Content = IconPickerView.Thumb(id, 20, new Thickness(0));
            name.Text = IconCatalogue.Find(id).Name;
        }
        Show(SettingsCatalogue.Read(_dbPath, row));
        _showIcon = Show;

        var b = new Button
        {
            Content = panel,
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(4, 2, 4, 2),
        };
        b.SetResourceReference(StyleProperty, "LinkButton");
        b.Click += (_, _) => OpenIconPicker();
        return b;
    }

    public void OpenIconPicker()
    {
        var pipe = PipeNames.For(Environment.GetEnvironmentVariable("TIMESINK_DB"));
        var picker = new IconPickerView(_dbPath, pipe, id => _showIcon?.Invoke(id));
        Grid.SetRowSpan(picker, 2);
        Panel.SetZIndex(picker, 5);
        Children.Add(picker);
    }

    public bool TryClosePicker()
    {
        if (Children.OfType<IconPickerView>().LastOrDefault() is not { } picker) return false;
        Children.Remove(picker);
        return true;
    }

    public bool TryCloseSubView()
    {
        if (_overlay is not null) { CloseDialog(); return true; }
        if (TryClosePicker()) return true;
        if (TryCloseDevicesSync()) return true;
        return false;
    }

    private FrameworkElement BuildButton(SettingRow row)
    {
        string label = row.Id switch
        {
            "backupNow" => "Run",
            "restore" => "Choose file…",
            "export" => "Export…",
            "installFolder" => "Open",
            "uninstall" => "Uninstall…",
            _ => "Delete…"
        };

        var b = new Button
        {
            Content = label,
            FontWeight = FontWeights.SemiBold,
            FontSize = 12,
            Padding = new Thickness(6, 3, 6, 3),
            VerticalAlignment = VerticalAlignment.Center,
        };
        b.SetResourceReference(StyleProperty, "LinkButton");
        b.SetResourceReference(Control.ForegroundProperty, row.Danger ? "Brush.Danger" : "Brush.Accent");

        switch (row.Id)
        {
            case "backupNow": b.Click += (_, _) => BackUpNow(b); break;
            case "restore": b.Click += (_, _) => ChooseRestoreFile(); break;
            case "export": b.Click += (_, _) => ChooseExport(); break;
            case "deleteHistory": b.Click += (_, _) => ConfirmDelete(); break;
            case "installFolder": b.Click += (_, _) => OpenInstallFolder(); break;
            case "uninstall": b.Click += (_, _) => ConfirmUninstall(); break;
        }
        return b;
    }

    private FrameworkElement BuildInfo(SettingRow row)
    {
        var t = new TextBlock
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        t.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        _info[row.Id] = t;
        return t;
    }

    private void Persist(SettingRow row, string value)
    {
        var path = _dbPath;
        SerialWriter.Run(() => SettingsCatalogue.Write(path, row, value));
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
        UpdateSyncStatus();
        UpdateDeviceName();
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
