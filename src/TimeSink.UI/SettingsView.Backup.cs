using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Globalization;
using Microsoft.Win32;
using TimeSink.Core.Backup;
using TimeSink.Core.Data;
using TimeSink.Core.Settings;
using TimeSink.Core.Usage;
using static TimeSink.UI.Services.TimeText;

namespace TimeSink.UI;

/// <summary>Backup and data rows: Back up now, Restore (Replace or Merge), Export, Delete all history.</summary>
public sealed partial class SettingsView
{
    private TextBlock? _backupDesc;
    private Grid? _overlay;

    private static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeSink", "backups");

    private static string Version => typeof(SettingsView).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private void RefreshLastBackup()
    {
        if (_backupDesc is null) return;
        var path = _dbPath;
        Task.Run(() => AppSettings.ForFile(path).Get(Setting.BackupLastUtc))
            .ContinueWith(t => _backupDesc.Text = LastBackupText.Describe(t.Result, DateTimeOffset.UtcNow),
                TaskScheduler.FromCurrentSynchronizationContext());
    }

    // ---- Back up now -----------------------------------------------------------------------------------

    private async void BackUpNow(Button button)
    {
        button.IsEnabled = false;
        button.Content = Str(SettingsStrings.BackingUp);
        var path = _dbPath;
        try
        {
            await Task.Run(() => AutoBackup.Now(path, DateTimeOffset.UtcNow, DefaultFolder, Version));
            button.Content = Str(SettingsStrings.BackupDone);
            RefreshLastBackup();
            await Task.Delay(2500);
        }
        catch (Exception ex) when (ex is IOException or Microsoft.Data.Sqlite.SqliteException or UnauthorizedAccessException)
        {
            button.Content = Str(SettingsStrings.BackupFailedShort);
            if (_backupDesc is not null) _backupDesc.Text = Str(SettingsStrings.BackupFailed, ex.Message);
            await Task.Delay(2500);
        }
        button.Content = Str(SettingsStrings.BackUp);
        button.IsEnabled = true;
    }

    // ---- Restore ---------------------------------------------------------------------------------------

    private async void ChooseRestoreFile()
    {
        var dlg = new OpenFileDialog { Title = Str(SettingsStrings.RestoreTitle), Filter = Str(SettingsStrings.RestoreFilter), InitialDirectory = Directory.Exists(DefaultFolder) ? DefaultFolder : "" };
        if (NativeDialog(() => dlg.ShowDialog(Window.GetWindow(this)) == true) is not true) return;
        var file = dlg.FileName;

        BackupInspection info;
        try { info = await Task.Run(() => RestoreService.Inspect(file)); }
        catch (Exception ex) when (ex is BackupException or Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            ShowDialog(Str(SettingsStrings.RestoreCannot), ex.Message, null, (Str(SettingsStrings.Ok), CloseDialog, false, false));
            return;
        }

        var madeAt = info.Manifest.CreatedUtc.ToLocalTime().DateTime;
        var made = madeAt.ToString("d MMM yyyy ", CultureInfo.CurrentCulture) + ClockFormat.Clock(madeAt, CultureInfo.CurrentCulture);
        var detail = Str(SettingsStrings.RestoreDetail, Path.GetFileName(file), made, info.Apps, info.Sessions.ToString("N0", CultureInfo.CurrentCulture)) +
                     (info.NeedsMigration ? Str(SettingsStrings.RestoreUpgrade) : "");
        ShowDialog(Str(SettingsStrings.RestoreTitle), detail + "\n\n" + Str(SettingsStrings.RestoreBody), null,
            (Str(SettingsStrings.Cancel), CloseDialog, false, false),
            (Str(SettingsStrings.Merge), () => RunRestore(file, replace: false), false, false),
            (Str(SettingsStrings.Replace), () => RunRestore(file, replace: true), true, false));
    }

    private async void RunRestore(string file, bool replace)
    {
        ShowDialog(Str(replace ? SettingsStrings.Replacing : SettingsStrings.Merging), Str(SettingsStrings.Restoring), null);
        var path = _dbPath;
        string summary;
        try
        {
            summary = await Task.Run(() =>
            {
                var zone = TimeZoneInfo.Local;
                if (replace)
                {
                    var r = RestoreService.Replace(file, path, AutoBackup.Folder(path, DefaultFolder), DateTimeOffset.UtcNow, zone);
                    SignalAgentReset();
                    return Str(SettingsStrings.Replaced) + (r.SafetyCopy is null ? "" : Str(SettingsStrings.SafetyCopy, Path.GetFileName(r.SafetyCopy)));
                }
                var m = RestoreService.Merge(file, path, zone);
                return Str(SettingsStrings.Merged, m.SessionsAdded.ToString("N0", CultureInfo.CurrentCulture), m.AppsAdded, m.SessionsSkipped.ToString("N0", CultureInfo.CurrentCulture));
            });
        }
        catch (Exception ex) when (ex is BackupException or Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            summary = Str(SettingsStrings.RestoreFailed, ex.Message);
        }
        ShowDialog(Str(replace ? SettingsStrings.ReplaceFinished : SettingsStrings.MergeFinished), summary, null, (Str(SettingsStrings.Ok), CloseDialog, true, false));
        RefreshInfo();
    }

    // ---- Export ----------------------------------------------------------------------------------------

    private void ChooseExport() =>
        ShowDialog(Str(SettingsStrings.ExportTitle), Str(SettingsStrings.ExportBody), null,
            (Str(SettingsStrings.Cancel), CloseDialog, false, false),
            (Str(SettingsStrings.Json), () => RunExport(csv: false), false, false),
            (Str(SettingsStrings.Csv), () => RunExport(csv: true), true, false));

    private async void RunExport(bool csv)
    {
        CloseDialog();
        var path = _dbPath;
        string? result = null;
        try
        {
            if (csv)
            {
                var dlg = new OpenFolderDialog { Title = Str(SettingsStrings.ExportFolderTitle) };
                if (NativeDialog(() => dlg.ShowDialog(Window.GetWindow(this)) == true) is not true) return;
                var folder = dlg.FolderName;
                var files = await Task.Run(() => DataExport.ExportCsv(path, folder));
                result = Str(SettingsStrings.ExportSavedTwo, Path.GetFileName(files.SessionsCsv), Path.GetFileName(files.DailyCsv), folder);
            }
            else
            {
                var dlg = new SaveFileDialog { Title = Str(SettingsStrings.ExportJsonTitle), FileName = "timesink-export.json", Filter = Str(SettingsStrings.ExportJsonFilter) };
                if (NativeDialog(() => dlg.ShowDialog(Window.GetWindow(this)) == true) is not true) return;
                var file = dlg.FileName;
                await Task.Run(() => DataExport.ExportJson(path, file));
                result = Str(SettingsStrings.ExportSaved, file);
            }
        }
        catch (Exception ex) when (ex is IOException or Microsoft.Data.Sqlite.SqliteException or UnauthorizedAccessException)
        {
            result = Str(SettingsStrings.ExportFailed, ex.Message);
        }
        ShowDialog(Str(SettingsStrings.ExportResultTitle), result, null, (Str(SettingsStrings.Ok), CloseDialog, true, false));
    }

    // ---- Delete all history ----------------------------------------------------------------------------

    private void ConfirmDelete()
    {
        var box = new TextBox { Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(6, 4, 6, 4) };
        box.SetResourceReference(Control.BackgroundProperty, "Brush.SurfaceAlt");
        box.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        box.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        var buttons = ShowDialog(Str(SettingsStrings.DeleteTitle), Str(SettingsStrings.DeleteBody),
            box, (Str(SettingsStrings.Cancel), CloseDialog, false, false), (Str(SettingsStrings.DeleteEverything), RunDelete, false, true));
        var delete = buttons[1];
        delete.IsEnabled = false;
        box.TextChanged += (_, _) => delete.IsEnabled = string.Equals(box.Text, HistoryDeleter.Confirmation, StringComparison.Ordinal);
        box.Focus();

        async void RunDelete()
        {
            var typed = box.Text;
            var path = _dbPath;
            string result;
            try
            {
                await Task.Run(() => { HistoryDeleter.DeleteAll(path, typed); SignalAgentReset(); });
                result = Str(SettingsStrings.DeleteDone);
            }
            catch (Exception ex) when (ex is ArgumentException or Microsoft.Data.Sqlite.SqliteException or IOException)
            {
                result = Str(SettingsStrings.DeleteFailed, ex.Message);
            }
            ShowDialog(Str(SettingsStrings.DeleteTitle), result, null, (Str(SettingsStrings.Ok), CloseDialog, true, false));
            RefreshInfo();
        }
    }

    // ---- Install folder and uninstall -------------------------------------------------------------------

    /// <summary>Where the installer put TimeSink on this PC (its uninstall registry entry, AppId from installer\TimeSink.iss), else the per-user default, else the folder running now.</summary>
    private static string InstallDir
    {
        get
        {
            const string key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{6F1D2C58-3B7E-4C0A-9A53-71D4E2B8C9A1}_is1";
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var k = hive.OpenSubKey(key);
                if (k?.GetValue("InstallLocation") is string dir && Directory.Exists(dir)) return dir.TrimEnd('\\', '/');
            }
            var perUser = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "TimeSink");
            return Directory.Exists(perUser) ? perUser : AppContext.BaseDirectory.TrimEnd('\\', '/');
        }
    }

    private void OpenInstallFolder()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{InstallDir}\"") { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            ShowDialog(Str(SettingsStrings.UninstallTitle), Str(SettingsStrings.OpenFailed, ex.Message), null, (Str(SettingsStrings.Ok), CloseDialog, true, false));
        }
    }

    private void ConfirmUninstall()
    {
        var uninstaller = Directory.EnumerateFiles(InstallDir, "unins*.exe").FirstOrDefault();
        if (uninstaller is null)
        {
            ShowDialog(Str(SettingsStrings.UninstallTitle), Str(SettingsStrings.UninstallMissing, InstallDir), null, (Str(SettingsStrings.Ok), CloseDialog, true, false));
            return;
        }
        ShowDialog(Str(SettingsStrings.UninstallTitle), Str(SettingsStrings.UninstallBody), null,
            (Str(SettingsStrings.Cancel), CloseDialog, false, false), (Str(SettingsStrings.UninstallConfirm), () =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uninstaller) { UseShellExecute = true });
                    Application.Current.Shutdown();
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    ShowDialog(Str(SettingsStrings.UninstallTitle), Str(SettingsStrings.OpenFailed, ex.Message), null, (Str(SettingsStrings.Ok), CloseDialog, true, false));
                }
            }, false, true));
    }

    // ---- helpers ---------------------------------------------------------------------------------------

    /// <summary>Tells the running Agent that the database changed underneath it (no-op when it is not running).</summary>
    private static void SignalAgentReset()
    {
        try { using var ev = EventWaitHandle.OpenExisting(AgentSignals.ResetEventName); ev.Set(); }
        catch (WaitHandleCannotBeOpenedException) { }
    }

    /// <summary>Runs a native dialog without letting the flyout close because it lost activation.</summary>
    private T NativeDialog<T>(Func<T> show)
    {
        var window = Window.GetWindow(this) as FlyoutWindow;
        if (window is not null) window.KeepOpen = true;
        try { return show(); }
        finally
        {
            if (window is not null) { window.KeepOpen = false; window.Activate(); }
        }
    }

    private Button[] ShowDialog(string title, string body, UIElement? extra, params (string Label, Action OnClick, bool Primary, bool Danger)[] buttons)
    {
        CloseDialog();
        var card = new StackPanel();
        var t = new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        t.SetResourceReference(TextBlock.StyleProperty, "Wordmark");
        card.Children.Add(t);
        var b = new TextBlock { Text = body, TextWrapping = TextWrapping.Wrap };
        b.SetResourceReference(TextBlock.StyleProperty, "Caption");
        b.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSoft");
        card.Children.Add(b);
        if (extra is not null) card.Children.Add(extra);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
        var made = new List<Button>();
        foreach (var (label, onClick, primary, danger) in buttons)
        {
            var btn = new Button { Content = label, Margin = new Thickness(6, 0, 0, 0) };
            btn.SetResourceReference(StyleProperty, "LinkButton");
            if (danger) btn.SetResourceReference(Control.ForegroundProperty, "Brush.Danger");
            else if (primary) btn.SetResourceReference(Control.ForegroundProperty, "Brush.Accent");
            btn.Click += (_, _) => onClick();
            row.Children.Add(btn);
            made.Add(btn);
        }
        card.Children.Add(row);

        var border = new Border { Padding = new Thickness(16), Margin = new Thickness(20), VerticalAlignment = VerticalAlignment.Center, BorderThickness = new Thickness(1), Child = card };
        border.SetResourceReference(Border.BackgroundProperty, "Brush.Panel");
        border.SetResourceReference(Border.BorderBrushProperty, "Brush.Border");
        border.SetResourceReference(Border.CornerRadiusProperty, "CornerRadius.Large");

        _overlay = new Grid();
        _overlay.SetResourceReference(Panel.BackgroundProperty, "Brush.Scrim");
        _overlay.Children.Add(border);
        Panel.SetZIndex(_overlay, 10);
        Children.Add(_overlay);
        return [.. made];
    }

    private void CloseDialog()
    {
        if (_overlay is null) return;
        Children.Remove(_overlay);
        _overlay = null;
    }
}
