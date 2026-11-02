using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Data.Sqlite;
using Microsoft.Win32;
using TimeSink.Core.Backup;
using TimeSink.Core.Data;
using TimeSink.Core.Devices;
using TimeSink.Core.Ipc;
using TimeSink.Core.Security;
using TimeSink.Core.Settings;
using TimeSink.Core.Sync;
using TimeSink.UI.Controls;
using TimeSink.UI.Services;
using static TimeSink.UI.Services.TimeText;

namespace TimeSink.UI;

public sealed partial class SettingsView
{
    private UIElement? _devicesSyncOverlay;

    public void OpenDevicesSync()
    {
        CloseDevicesSync();
        var view = BuildDevicesSyncView();
        _devicesSyncOverlay = view;
        Grid.SetRowSpan(view, 2);
        Panel.SetZIndex(view, 5);
        Children.Add(view);
    }

    public bool TryCloseDevicesSync()
    {
        if (_devicesSyncOverlay is null) return false;
        CloseDevicesSync();
        return true;
    }

    private void CloseDevicesSync()
    {
        if (_devicesSyncOverlay is null) return;
        Children.Remove(_devicesSyncOverlay);
        _devicesSyncOverlay = null;
        RefreshInfo();
    }

    private UIElement BuildDevicesSyncView()
    {
        var root = new Grid();
        root.SetResourceReference(Panel.BackgroundProperty, "Brush.Surface");

        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Body
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

        // 1. Header with back button and title
        var header = new Grid { Height = 44, Margin = new Thickness(12, 0, 12, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var backBtn = new Button
        {
            Content = "‹ Settings",
            Height = 28,
            Padding = new Thickness(6, 0, 8, 0),
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        backBtn.SetResourceReference(StyleProperty, "LinkButton");
        backBtn.SetResourceReference(Control.ForegroundProperty, "Brush.Accent");
        backBtn.Click += (_, _) => CloseDevicesSync();
        Grid.SetColumn(backBtn, 0);
        header.Children.Add(backBtn);

        var title = new TextBlock
        {
            Text = Str(SettingsStrings.DevicesSyncTitle),
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
        Grid.SetColumn(title, 1);
        header.Children.Add(title);

        var headerBorder = new Border
        {
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = header,
        };
        headerBorder.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");
        Grid.SetRow(headerBorder, 0);
        root.Children.Add(headerBorder);

        // 2. Body
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false };
        var stack = new StackPanel { Margin = new Thickness(16, 12, 16, 16) };
        scroll.Content = stack;
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        // 3. Footer: Last sync and Sync now button
        var footer = new Grid { Height = 44, Margin = new Thickness(16, 0, 16, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var lastSyncText = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        lastSyncText.SetResourceReference(TextBlock.StyleProperty, "Caption");
        lastSyncText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
        Grid.SetColumn(lastSyncText, 0);
        footer.Children.Add(lastSyncText);

        var syncNowBtn = new Button
        {
            Content = Str(SettingsStrings.SyncNow),
            Height = 28,
            Padding = new Thickness(14, 0, 14, 0),
            FontWeight = FontWeights.SemiBold,
            Cursor = System.Windows.Input.Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center,
        };
        syncNowBtn.SetResourceReference(Control.BackgroundProperty, "Brush.Accent");
        syncNowBtn.SetResourceReference(Control.ForegroundProperty, "Brush.OnAccent");
        syncNowBtn.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");
        Grid.SetColumn(syncNowBtn, 1);
        footer.Children.Add(syncNowBtn);

        var footerBorder = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = footer,
        };
        footerBorder.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");
        Grid.SetRow(footerBorder, 2);
        root.Children.Add(footerBorder);

        void Refresh()
        {
            stack.Children.Clear();

            var settings = AppSettings.ForFile(_dbPath);
            var enabled = settings.Get(Setting.SyncEnabled);
            var folder = settings.Get(Setting.SyncFolder) ?? "";
            var syncState = settings.Get(Setting.SyncState) ?? "";
            var lastExport = settings.Get(Setting.SyncLastExportUtc);
            var lastImport = settings.Get(Setting.SyncLastImportUtc);
            var lastSyncUtc = Math.Max(lastExport, lastImport);
            lastSyncText.Text = Str(SettingsStrings.LastSync, TimeAgo(lastSyncUtc));

            // Card 1: Sync folder card
            var folderCard = new Border
            {
                Padding = new Thickness(12),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 14),
            };
            folderCard.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
            folderCard.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            folderCard.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");

            var folderStack = new StackPanel();

            var topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var folderTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var folderTitle = new TextBlock { Text = Str(SettingsStrings.SyncFolderTitle), FontWeight = FontWeights.SemiBold };
            folderTitle.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            var folderDesc = new TextBlock { Text = Str(SettingsStrings.SyncFolderDesc), Margin = new Thickness(0, 2, 0, 0) };
            folderDesc.SetResourceReference(TextBlock.StyleProperty, "Caption");
            folderDesc.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            folderTexts.Children.Add(folderTitle);
            folderTexts.Children.Add(folderDesc);
            Grid.SetColumn(folderTexts, 0);
            topRow.Children.Add(folderTexts);

            var toggle = new ToggleButton { IsChecked = enabled, VerticalAlignment = VerticalAlignment.Center };
            toggle.SetResourceReference(StyleProperty, "ToggleSwitch");
            toggle.Click += (_, _) =>
            {
                var on = toggle.IsChecked == true;
                settings.Set(Setting.SyncEnabled, on);
                if (on && settings.Get(Setting.SyncEnabledUtc) == 0)
                    settings.Set(Setting.SyncEnabledUtc, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                TriggerSyncNow();
                Refresh();
            };
            Grid.SetColumn(toggle, 1);
            topRow.Children.Add(toggle);
            folderStack.Children.Add(topRow);

            // Folder path box
            var pathBox = new Border
            {
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(8, 6, 8, 6),
                BorderThickness = new Thickness(1),
            };
            pathBox.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
            pathBox.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            pathBox.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");

            var pathGrid = new Grid();
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            pathGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var folderIcon = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M1.5 4.5 A1 1 0 0 1 2.5 3.5 L6 3.5 L7.5 5 L13.5 5 A1 1 0 0 1 14.5 6 L14.5 12.5 A1 1 0 0 1 13.5 13.5 L2.5 13.5 A1 1 0 0 1 1.5 12.5 Z"),
                StrokeThickness = 1.5,
                Width = 14,
                Height = 14,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            folderIcon.SetResourceReference(Shape.StrokeProperty, "Brush.TextMuted");
            Grid.SetColumn(folderIcon, 0);
            pathGrid.Children.Add(folderIcon);

            var pathText = new TextBlock
            {
                Text = !string.IsNullOrWhiteSpace(folder) ? folder : "No sync folder selected",
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            pathText.SetResourceReference(TextBlock.StyleProperty, "Caption");
            pathText.SetResourceReference(TextBlock.ForegroundProperty, !string.IsNullOrWhiteSpace(folder) ? "Brush.Text" : "Brush.TextMuted");
            Grid.SetColumn(pathText, 1);
            pathGrid.Children.Add(pathText);

            var changeBtn = new Button
            {
                Content = Str(SettingsStrings.SyncChange),
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 0, 0),
            };
            changeBtn.SetResourceReference(StyleProperty, "LinkButton");
            changeBtn.SetResourceReference(Control.ForegroundProperty, "Brush.Accent");
            changeBtn.Click += (_, _) =>
            {
                var dlg = new OpenFolderDialog
                {
                    Title = "Choose Sync Folder",
                    InitialDirectory = Directory.Exists(folder) ? folder : "",
                };
                if (NativeDialog(() => dlg.ShowDialog(Window.GetWindow(this)) == true) && !string.IsNullOrWhiteSpace(dlg.FolderName))
                {
                    settings.Set(Setting.SyncFolder, dlg.FolderName);
                    TriggerSyncNow();
                    Refresh();
                }
            };
            Grid.SetColumn(changeBtn, 2);
            pathGrid.Children.Add(changeBtn);

            pathBox.Child = pathGrid;
            folderStack.Children.Add(pathBox);
            folderCard.Child = folderStack;
            stack.Children.Add(folderCard);

            // -------------------------------------------------------------------------------------------------
            // Card 1.5: Sync Encryption Banner & Configuration Card
            // -------------------------------------------------------------------------------------------------

            // 1. Guidance banner when in paused encryption error states
            if (syncState is "encryption_key_wrong" or "encryption_key_missing" or "remote_not_encrypted")
            {
                var (bannerTitle, bannerDesc, isDanger) = syncState switch
                {
                    "encryption_key_wrong" => (
                        "Sync Paused — Passphrase Mismatch",
                        "The entered passphrase does not match the other device. Make sure both devices use the exact same passphrase to decrypt sync archives.",
                        true),
                    "encryption_key_missing" => (
                        "Sync Paused — Passphrase Required",
                        "The remote sync file is encrypted, but no passphrase is configured on this device. Enter your sync passphrase below to resume synchronization.",
                        false),
                    "remote_not_encrypted" => (
                        "Sync Alert — Remote Device Not Encrypted",
                        "This device expects encrypted sync files, but the remote archive is unencrypted. Please update TimeSink and configure the same passphrase on the other device.",
                        false),
                    _ => ("", "", false)
                };

                var warnBanner = new Border
                {
                    Padding = new Thickness(12, 10, 12, 10),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 0, 14),
                };
                warnBanner.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
                warnBanner.SetResourceReference(Border.BorderBrushProperty, isDanger ? "Brush.Danger" : "Brush.Warning");
                warnBanner.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");

                var warnStack = new StackPanel();

                var titleBlock = new TextBlock
                {
                    Text = bannerTitle,
                    FontWeight = FontWeights.SemiBold,
                };
                titleBlock.SetResourceReference(TextBlock.ForegroundProperty, isDanger ? "Brush.Danger" : "Brush.Warning");
                warnStack.Children.Add(titleBlock);

                var descBlock = new TextBlock
                {
                    Text = bannerDesc,
                    Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 16,
                };
                descBlock.SetResourceReference(TextBlock.StyleProperty, "Caption");
                descBlock.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
                descBlock.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
                warnStack.Children.Add(descBlock);

                warnBanner.Child = warnStack;
                stack.Children.Add(warnBanner);
            }

            // 2. Section Header: SYNC ENCRYPTION
            var encHeader = new TextBlock
            {
                Text = "SYNC ENCRYPTION",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(4, 0, 0, 6),
            };
            encHeader.SetResourceReference(TextBlock.StyleProperty, "Caption");
            encHeader.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            stack.Children.Add(encHeader);

            // 3. Card: Sync Encryption Card
            var encCard = new Border
            {
                Padding = new Thickness(12),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 14),
            };
            encCard.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
            encCard.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            encCard.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");

            var encStack = new StackPanel();

            // Row 1: Header with Vector Lock Icon and Status Badge
            var encTopRow = new Grid();
            encTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            encTopRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var encTitleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var isEncrypted = settings.Get(Setting.SyncEncrypted);
            var hasPassphrase = Db.KeyManager.HasSyncPassphrase();

            var lockIcon = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M4 5 A3 3 0 0 1 10 5 L10 7 L11 7 A1 1 0 0 1 12 8 L12 13 A1 1 0 0 1 11 14 L3 14 A1 1 0 0 1 2 13 L2 8 A1 1 0 0 1 3 7 L4 7 Z M5 7 L9 7 L9 5 A2 2 0 0 0 5 5 Z"),
                StrokeThickness = 1.3,
                Width = 14,
                Height = 14,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            lockIcon.SetResourceReference(Shape.StrokeProperty, isEncrypted && hasPassphrase ? "Brush.Accent" : "Brush.TextMuted");
            encTitleStack.Children.Add(lockIcon);

            var encTitle = new TextBlock
            {
                Text = "Snapshot Encryption (SQLCipher 4)",
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            encTitle.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
            encTitleStack.Children.Add(encTitle);

            Grid.SetColumn(encTitleStack, 0);
            encTopRow.Children.Add(encTitleStack);

            // Status Badge
            var statusBorder = new Border
            {
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center,
            };
            statusBorder.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
            statusBorder.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");

            var statusText = new TextBlock
            {
                FontSize = 11,
                FontWeight = FontWeights.Medium,
            };

            if (isEncrypted && hasPassphrase)
            {
                statusText.Text = "● Encrypted";
                statusText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            }
            else
            {
                statusText.Text = "○ Not configured";
                statusText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            }

            statusBorder.Child = statusText;
            Grid.SetColumn(statusBorder, 1);
            encTopRow.Children.Add(statusBorder);

            encStack.Children.Add(encTopRow);

            // Row 2: Help text
            var encHelp = new TextBlock
            {
                Text = "Enter the same passphrase on both devices to encrypt your sync data. Your passphrase is stored securely in Windows DPAPI and never sent over the network.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 10),
                LineHeight = 16,
            };
            encHelp.SetResourceReference(TextBlock.StyleProperty, "Caption");
            encHelp.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
            encHelp.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            encStack.Children.Add(encHelp);

            // Row 3: PasswordBox + Action Button
            var inputGrid = new Grid();
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inputGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var pwdBox = new PasswordBox
            {
                Height = 28,
                Padding = new Thickness(8, 4, 8, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            };
            pwdBox.SetResourceReference(Control.BackgroundProperty, "Brush.SurfaceAlt");
            pwdBox.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
            pwdBox.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");

            Grid.SetColumn(pwdBox, 0);
            inputGrid.Children.Add(pwdBox);

            var setBtn = new Button
            {
                Content = hasPassphrase ? "Update Passphrase" : "Set Passphrase",
                Height = 28,
                Padding = new Thickness(14, 0, 14, 0),
                FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center,
                IsEnabled = false,
            };
            setBtn.SetResourceReference(Control.BackgroundProperty, "Brush.Accent");
            setBtn.SetResourceReference(Control.ForegroundProperty, "Brush.OnAccent");
            setBtn.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");

            pwdBox.PasswordChanged += (_, _) =>
            {
                setBtn.IsEnabled = !string.IsNullOrWhiteSpace(pwdBox.Password);
            };

            pwdBox.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter && setBtn.IsEnabled)
                {
                    setBtn.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                }
            };

            setBtn.Click += async (_, _) =>
            {
                var pwd = pwdBox.Password;
                if (string.IsNullOrWhiteSpace(pwd)) return;

                setBtn.IsEnabled = false;

                // 1. Store via IKeyManager locally (DPAPI)
                Db.KeyManager.SetSyncPassphrase(pwd);

                // 2. Set sync.encrypted = true
                settings.Set(Setting.SyncEncrypted, true);

                // 3. Clear paused error states
                if (syncState is "encryption_key_missing" or "encryption_key_wrong" or "remote_not_encrypted")
                {
                    settings.Set(Setting.SyncState, "pending");
                }

                // 4. Send IPC message to Agent
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var scratch = Environment.GetEnvironmentVariable("TIMESINK_DB");
                        var pipeName = PipeNames.For(scratch);
                        await IpcClient.SendAsync(pipeName, new SetSyncPassphraseMessage(pwd));
                    }
                    catch { }
                });

                // 5. Trigger immediate sync
                TriggerSyncNow();

                // 6. Refresh UI
                Refresh();
            };

            Grid.SetColumn(setBtn, 1);
            inputGrid.Children.Add(setBtn);

            encStack.Children.Add(inputGrid);
            encCard.Child = encStack;
            stack.Children.Add(encCard);

            // Card 2: Devices section
            var devHeader = new TextBlock
            {
                Text = "DEVICES",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(4, 0, 0, 6),
            };
            devHeader.SetResourceReference(TextBlock.StyleProperty, "Caption");
            devHeader.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Accent");
            stack.Children.Add(devHeader);

            var devicesCard = new Border
            {
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 14),
            };
            devicesCard.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
            devicesCard.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
            devicesCard.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");

            var devicesList = new StackPanel();
            var devices = ReadDevices();
            var activeRemoteCount = devices.Count(d => !d.IsLocal && d.State == "active");

            for (var i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                var rowBorder = new Border
                {
                    Padding = new Thickness(12),
                    BorderThickness = new Thickness(0, i == 0 ? 0 : 1, 0, 0),
                };
                rowBorder.SetResourceReference(Border.BorderBrushProperty, "Brush.BorderSoft");

                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Badge: PC / Mac
                var badge = new Border
                {
                    Width = 34,
                    Height = 34,
                    CornerRadius = new CornerRadius(8),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                badge.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
                badge.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
                var badgeText = new TextBlock
                {
                    Text = d.Platform == "macos" ? "Mac" : "PC",
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                badgeText.SetResourceReference(TextBlock.ForegroundProperty, d.IsLocal ? "Brush.DeviceA" : "Brush.DeviceB");
                badge.Child = badgeText;
                Grid.SetColumn(badge, 0);
                rowGrid.Children.Add(badge);

                // Middle: Device name & details
                var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var nameText = new TextBlock
                {
                    Text = d.Name + (d.IsLocal ? " · this PC" : ""),
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                };
                nameText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
                details.Children.Add(nameText);

                var line1 = new TextBlock
                {
                    Text = (d.Platform == "macos" ? "macOS" : "Windows") + $" · {GetDeviceAppCount(d.DeviceId)} apps",
                    Margin = new Thickness(0, 2, 0, 0),
                };
                line1.SetResourceReference(TextBlock.StyleProperty, "Caption");
                line1.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
                line1.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
                details.Children.Add(line1);

                string line2String;
                if (d.IsLocal)
                {
                    line2String = lastExport > 0 ? $"Wrote its file {TimeAgo(lastExport)}" : "Ready to write file";
                }
                else if (d.State == "forgotten")
                {
                    line2String = "Sync paused · forgotten";
                }
                else if (d.LastImportUtc.HasValue)
                {
                    var hours = GetDeviceHours(d.DeviceId);
                    line2String = $"Read its file {TimeAgo(d.LastImportUtc.Value.ToUnixTimeSeconds())} · {hours}h";
                }
                else if (syncState == "pending")
                {
                    line2String = "Waiting for OneDrive to download its file";
                }
                else
                {
                    line2String = "Waiting for file to download";
                }

                var line2 = new TextBlock { Text = line2String, Margin = new Thickness(0, 1, 0, 0) };
                line2.SetResourceReference(TextBlock.StyleProperty, "Caption");
                line2.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
                line2.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
                details.Children.Add(line2);

                Grid.SetColumn(details, 1);
                rowGrid.Children.Add(details);

                // Right side: status badge and action menu/buttons
                var rightStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };

                var statusRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var statusDot = new Ellipse { Width = 6, Height = 6, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                var statusLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
                statusLabel.SetResourceReference(TextBlock.StyleProperty, "Caption");
                statusLabel.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");

                if (d.State == "forgotten")
                {
                    statusDot.SetResourceReference(Shape.FillProperty, "Brush.TextMuted");
                    statusLabel.Text = Str(SettingsStrings.ForgetDevice);
                    statusLabel.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
                }
                else if (d.IsLocal)
                {
                    statusDot.SetResourceReference(Shape.FillProperty, "Brush.DeviceA");
                    statusLabel.Text = Str(SettingsStrings.UpToDate);
                    statusLabel.SetResourceReference(TextBlock.ForegroundProperty, "Brush.DeviceA");
                }
                else if (activeRemoteCount > 1 && i >= 2)
                {
                    statusDot.SetResourceReference(Shape.FillProperty, "Brush.Warning");
                    statusLabel.Text = Str(SettingsStrings.OnlyTwoSupported);
                    statusLabel.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Warning");
                }
                else if (syncState == "no_other_device")
                {
                    statusDot.SetResourceReference(Shape.FillProperty, "Brush.Warning");
                    statusLabel.Text = Str(SettingsStrings.NoOtherFound);
                    statusLabel.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Warning");
                }
                else if (syncState == "pending")
                {
                    statusDot.SetResourceReference(Shape.FillProperty, "Brush.Warning");
                    statusLabel.Text = Str(SettingsStrings.Pending);
                    statusLabel.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Warning");
                }
                else
                {
                    statusDot.SetResourceReference(Shape.FillProperty, "Brush.DeviceA");
                    statusLabel.Text = Str(SettingsStrings.Synced);
                    statusLabel.SetResourceReference(TextBlock.ForegroundProperty, "Brush.DeviceA");
                }

                statusRow.Children.Add(statusDot);
                statusRow.Children.Add(statusLabel);
                rightStack.Children.Add(statusRow);

                // Actions: Rename, Forget, Delete history
                var actionsRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };

                var renameBtn = new Button
                {
                    Content = "Rename",
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Margin = new Thickness(6, 0, 0, 0),
                };
                renameBtn.SetResourceReference(StyleProperty, "LinkButton");
                renameBtn.SetResourceReference(Control.ForegroundProperty, "Brush.TextMuted");
                var currentDev = d;
                renameBtn.Click += (_, _) => PromptRenameDevice(currentDev, Refresh);
                actionsRow.Children.Add(renameBtn);

                if (!d.IsLocal)
                {
                    if (d.State == "active")
                    {
                        var forgetBtn = new Button
                        {
                            Content = "Forget",
                            Cursor = System.Windows.Input.Cursors.Hand,
                            Margin = new Thickness(6, 0, 0, 0),
                        };
                        forgetBtn.SetResourceReference(StyleProperty, "LinkButton");
                        forgetBtn.SetResourceReference(Control.ForegroundProperty, "Brush.TextMuted");
                        forgetBtn.Click += (_, _) =>
                        {
                            using var db = Db.Open(_dbPath);
                            DeviceIdentity.ForgetDevice(db, currentDev.DeviceId);
                            Refresh();
                        };
                        actionsRow.Children.Add(forgetBtn);
                    }

                    var deleteBtn = new Button
                    {
                        Content = "Delete history…",
                        Cursor = System.Windows.Input.Cursors.Hand,
                        Margin = new Thickness(6, 0, 0, 0),
                    };
                    deleteBtn.SetResourceReference(StyleProperty, "LinkButton");
                    deleteBtn.SetResourceReference(Control.ForegroundProperty, "Brush.Danger");
                    deleteBtn.Click += (_, _) => PromptDeleteDeviceHistory(currentDev, Refresh);
                    actionsRow.Children.Add(deleteBtn);
                }

                rightStack.Children.Add(actionsRow);
                Grid.SetColumn(rightStack, 2);
                rowGrid.Children.Add(rightStack);

                rowBorder.Child = rowGrid;
                devicesList.Children.Add(rowBorder);
            }

            devicesCard.Child = devicesList;
            stack.Children.Add(devicesCard);

            // Card: Manage synced apps (visible when sync is enabled, remote devices exist, or synced apps exist)
            var hasRemote = devices.Any(d => !d.IsLocal && d.State != "forgotten");
            var hasSyncedApps = HasRemoteAppsInDb(_dbPath);
            if (enabled || hasRemote || hasSyncedApps)
            {
                var syncedAppsCard = new Border
                {
                    Padding = new Thickness(14, 12, 14, 12),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 0, 14),
                    Cursor = System.Windows.Input.Cursors.Hand,
                };
                syncedAppsCard.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
                syncedAppsCard.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
                syncedAppsCard.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");

                var cardGrid = new Grid();
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                cardGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var textStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var cardTitle = new TextBlock
                {
                    Text = "Manage synced apps",
                    FontWeight = FontWeights.SemiBold,
                };
                cardTitle.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
                var cardDesc = new TextBlock
                {
                    Text = "View and configure apps tracked across your devices",
                    Margin = new Thickness(0, 2, 0, 0),
                };
                cardDesc.SetResourceReference(TextBlock.StyleProperty, "Caption");
                cardDesc.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
                cardDesc.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");

                textStack.Children.Add(cardTitle);
                textStack.Children.Add(cardDesc);
                Grid.SetColumn(textStack, 0);
                cardGrid.Children.Add(textStack);

                var chevron = new TextBlock
                {
                    Text = "›",
                    FontSize = 18,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(8, 0, 0, 0),
                };
                chevron.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
                Grid.SetColumn(chevron, 1);
                cardGrid.Children.Add(chevron);

                syncedAppsCard.Child = cardGrid;

                syncedAppsCard.MouseEnter += (_, _) => syncedAppsCard.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
                syncedAppsCard.MouseLeave += (_, _) => syncedAppsCard.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");

                syncedAppsCard.MouseLeftButtonUp += (_, _) =>
                {
                    var win = Window.GetWindow(this) as FlyoutWindow;
                    win?.OpenAddAppTrackedSynced(returnToDevicesSync: true);
                };

                stack.Children.Add(syncedAppsCard);
            }

            // Card 3: Merge hint banner (if non-merged apps with same name exist on multiple devices)
            var matchingApps = FindMatchingCrossDeviceApps();
            if (matchingApps.Count > 0)
            {
                var mergeBanner = new Border
                {
                    Padding = new Thickness(12, 10, 12, 10),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 0, 14),
                };
                mergeBanner.SetResourceReference(Border.BackgroundProperty, "Brush.SurfaceAlt");
                mergeBanner.SetResourceReference(Border.BorderBrushProperty, "Brush.Warning");
                mergeBanner.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Medium");

                var bannerGrid = new Grid();
                bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var bannerTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                var bannerTitle = new TextBlock
                {
                    Text = Str(SettingsStrings.MergeHintBoth, matchingApps.Count),
                    FontWeight = FontWeights.SemiBold,
                };
                bannerTitle.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Warning");
                var bannerDesc = new TextBlock
                {
                    Text = Str(SettingsStrings.MergeHintDesc, string.Join(", ", matchingApps.Take(3))),
                    Margin = new Thickness(0, 2, 0, 0),
                };
                bannerDesc.SetResourceReference(TextBlock.StyleProperty, "Caption");
                bannerDesc.SetResourceReference(TextBlock.ForegroundProperty, "Brush.Text");
                bannerTexts.Children.Add(bannerTitle);
                bannerTexts.Children.Add(bannerDesc);
                Grid.SetColumn(bannerTexts, 0);
                bannerGrid.Children.Add(bannerTexts);

                var mergeActionBtn = new Button
                {
                    Content = Str(SettingsStrings.MergeButton),
                    Height = 28,
                    Padding = new Thickness(12, 0, 12, 0),
                    FontWeight = FontWeights.SemiBold,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0),
                };
                mergeActionBtn.SetResourceReference(Control.BackgroundProperty, "Brush.Warning");
                mergeActionBtn.SetResourceReference(Control.ForegroundProperty, "Brush.Surface");
                mergeActionBtn.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Small");
                mergeActionBtn.Click += (_, _) =>
                {
                    PromptMergeCrossDeviceApps(matchingApps, Refresh);
                };
                Grid.SetColumn(mergeActionBtn, 1);
                bannerGrid.Children.Add(mergeActionBtn);

                mergeBanner.Child = bannerGrid;
                stack.Children.Add(mergeBanner);
            }

            // Footnote
            var footnote = new TextBlock
            {
                Text = Str(SettingsStrings.SyncFootnote),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 0, 2, 8),
                LineHeight = 16,
            };
            footnote.SetResourceReference(TextBlock.StyleProperty, "Caption");
            footnote.SetResourceReference(TextBlock.FontSizeProperty, "Double.FontSize.Small");
            footnote.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextMuted");
            stack.Children.Add(footnote);
        }

        syncNowBtn.Click += async (_, _) =>
        {
            syncNowBtn.IsEnabled = false;
            syncNowBtn.Content = Str(SettingsStrings.Syncing);
            await Task.Run(TriggerSyncNow);
            syncNowBtn.Content = Str(SettingsStrings.Synced) + " ✓";
            Refresh();
            await Task.Delay(1200);
            syncNowBtn.Content = Str(SettingsStrings.SyncNow);
            syncNowBtn.IsEnabled = true;
        };

        Refresh();
        return root;
    }

    private void TriggerSyncNow()
    {
        try
        {
            using var ev = EventWaitHandle.OpenExisting(AgentSignals.SyncNowEventName);
            ev.Set();
        }
        catch { }

        // Also perform direct sync export/import if sync folder is configured
        try
        {
            var settings = AppSettings.ForFile(_dbPath);
            if (settings.Get(Setting.SyncEnabled))
            {
                var folder = settings.Get(Setting.SyncFolder);
                if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                {
                    SyncWriter.Write(_dbPath, folder, DateTimeOffset.UtcNow);
                    var files = Directory.GetFiles(folder, "*" + SyncWriter.Extension);
                    foreach (var file in files)
                    {
                        if (SyncReader.IsConflictCopy(file)) continue;
                        if (SyncReader.IsPlaceholder(file)) continue;
                        SyncReader.Import(file, _dbPath, TimeZoneInfo.Local);
                    }
                    using var autoMergeDb = Db.Open(_dbPath);
                    DeviceIdentity.MergeMatchingCrossDeviceApps(autoMergeDb);
                }
            }
        }
        catch { }
    }

    private void PromptMergeCrossDeviceApps(IReadOnlyList<string> matchingApps, Action onDone)
    {
        var appListStr = string.Join(", ", matchingApps.Take(3)) + (matchingApps.Count > 3 ? $" and {matchingApps.Count - 3} more" : "");
        var body = $"Merge {matchingApps.Count} {(matchingApps.Count == 1 ? "app" : "apps")} ({appListStr}) across devices so their time is combined into single tracked apps?\n\nRemote instances will be merged under this device’s apps.";

        ShowDialog("Merge cross-device apps", body, null,
            (Str(SettingsStrings.Cancel), CloseDialog, false, false),
            ("Merge", () =>
            {
                using var db = Db.Open(_dbPath);
                DeviceIdentity.MergeMatchingCrossDeviceApps(db);
                CloseDialog();
                TriggerSyncNow();
                onDone();
            }, true, false));
    }

    private void PromptRenameDevice(DeviceRecord dev, Action onDone)
    {
        var box = new TextBox
        {
            Text = dev.Name,
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(6, 4, 6, 4),
        };
        box.SetResourceReference(Control.BackgroundProperty, "Brush.SurfaceAlt");
        box.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        box.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");

        var buttons = ShowDialog(Str(SettingsStrings.RenameDevice), "Enter a new name for this device:",
            box,
            (Str(SettingsStrings.Cancel), CloseDialog, false, false),
            ("Save", () =>
            {
                var newName = box.Text.Trim();
                if (!string.IsNullOrEmpty(newName))
                {
                    using var db = Db.Open(_dbPath);
                    DeviceIdentity.RenameDevice(db, dev.DeviceId, newName);
                }
                CloseDialog();
                onDone();
            }, true, false));

        box.SelectAll();
        box.Focus();
    }

    private void PromptDeleteDeviceHistory(DeviceRecord dev, Action onDone)
    {
        var box = new TextBox
        {
            Margin = new Thickness(0, 10, 0, 0),
            Padding = new Thickness(6, 4, 6, 4),
        };
        box.SetResourceReference(Control.BackgroundProperty, "Brush.SurfaceAlt");
        box.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        box.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");

        var promptBody = $"This permanently deletes all usage history and tracked apps recorded for “{dev.Name}”.\n\n{Str(SettingsStrings.DeleteDeviceHistoryConfirm)}";
        var buttons = ShowDialog(Str(SettingsStrings.DeleteDeviceHistory), promptBody,
            box,
            (Str(SettingsStrings.Cancel), CloseDialog, false, false),
            ("Delete", () =>
            {
                using var db = Db.Open(_dbPath);
                DeviceIdentity.DeleteDeviceHistory(db, dev.DeviceId);
                CloseDialog();
                onDone();
            }, false, true));

        var deleteBtn = buttons[1];
        deleteBtn.IsEnabled = false;
        box.TextChanged += (_, _) => deleteBtn.IsEnabled = string.Equals(box.Text.Trim(), "DELETE", StringComparison.Ordinal);
        box.Focus();
    }

    internal static bool HasRemoteAppsInDb(string dbPath)
    {
        try
        {
            using var conn = Db.CreateConnection(dbPath, SqliteOpenMode.ReadOnly);

            var localDevId = DeviceInfoService.GetLocalDeviceId(dbPath);

            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT 1 FROM apps
                WHERE (
                    (device_id IS NOT NULL AND device_id != '' AND ($localId IS NULL OR device_id != $localId))
                    OR id IN (
                        SELECT canonical_id FROM apps
                        WHERE canonical_id IS NOT NULL
                          AND device_id IS NOT NULL
                          AND device_id != ''
                          AND ($localId IS NULL OR device_id != $localId)
                    )
                )
                AND state = 'tracked'
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$localId", (object?)localDevId ?? DBNull.Value);
            var res = cmd.ExecuteScalar();
            return res != null && res != DBNull.Value;
        }
        catch { return false; }
    }

    private static DateTimeOffset? SafeParseDate(object? val)
    {
        if (val is null or DBNull) return null;
        if (val is long l) return DateTimeOffset.FromUnixTimeSeconds(l);
        if (val is int i) return DateTimeOffset.FromUnixTimeSeconds(i);
        var str = val.ToString();
        if (string.IsNullOrWhiteSpace(str)) return null;
        if (long.TryParse(str, out var unixSecs)) return DateTimeOffset.FromUnixTimeSeconds(unixSecs);
        if (DateTimeOffset.TryParse(str, out var dto)) return dto;
        return null;
    }

    private IReadOnlyList<DeviceRecord> ReadDevices()
    {
        var list = new List<DeviceRecord>();
        try
        {
            using var conn = Db.CreateConnection(_dbPath, SqliteOpenMode.ReadOnly);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT device_id, name, platform, is_local, first_seen_utc, last_import_utc, last_file_utc, state FROM devices ORDER BY is_local DESC, first_seen_utc ASC;";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var id = r.IsDBNull(0) ? "" : r.GetString(0);
                var name = r.IsDBNull(1) ? "" : r.GetString(1);
                var platform = r.IsDBNull(2) ? "windows" : r.GetString(2);
                var isLocal = !r.IsDBNull(3) && r.GetInt64(3) == 1;
                var firstSeen = SafeParseDate(r.GetValue(4)) ?? DateTimeOffset.UtcNow;
                DateTimeOffset? lastImport = SafeParseDate(r.GetValue(5));
                DateTimeOffset? lastFile = SafeParseDate(r.GetValue(6));
                var state = r.IsDBNull(7) ? "active" : r.GetString(7);
                list.Add(new DeviceRecord(id, name, platform, isLocal, firstSeen, lastImport, lastFile, state));
            }
        }
        catch { }
        return list;
    }

    private int GetDeviceAppCount(string deviceId)
    {
        try
        {
            using var conn = Db.CreateConnection(_dbPath, SqliteOpenMode.ReadOnly);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM apps WHERE device_id = $id AND canonical_id IS NULL AND state = 'tracked';";
            cmd.Parameters.AddWithValue("$id", deviceId);
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        }
        catch { return 0; }
    }

    private long GetDeviceHours(string deviceId)
    {
        try
        {
            using var conn = Db.CreateConnection(_dbPath, SqliteOpenMode.ReadOnly);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(SUM(seconds), 0) FROM daily_usage WHERE app_id IN (SELECT id FROM apps WHERE device_id = $id);";
            cmd.Parameters.AddWithValue("$id", deviceId);
            var totalSeconds = Convert.ToInt64(cmd.ExecuteScalar() ?? 0L);
            return totalSeconds / 3600;
        }
        catch { return 0; }
    }

    private List<string> FindMatchingCrossDeviceApps()
    {
        var result = new List<string>();
        try
        {
            using var conn = Db.CreateConnection(_dbPath, SqliteOpenMode.ReadOnly);
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT display_name FROM apps
                WHERE canonical_id IS NULL AND state = 'tracked'
                GROUP BY display_name
                HAVING COUNT(*) > 1;
                """;
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                result.Add(r.GetString(0));
            }
        }
        catch { }
        return result;
    }

    private static string TimeAgo(long utcSeconds)
    {
        if (utcSeconds <= 0) return "never";
        var span = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(utcSeconds);
        if (span.TotalSeconds < 60) return "just now";
        if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} min ago";
        if (span.TotalHours < 24) return $"{(int)span.TotalHours} h ago";
        return $"{(int)span.TotalDays} d ago";
    }
}
