using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using TimeSink.Core.Backup;
using TimeSink.Core.Data;

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
        Task.Run(() => long.TryParse(SettingsStore.Get(path, "backup.lastUtc"), out var l) ? l : 0)
            .ContinueWith(t => _backupDesc.Text = LastBackupText.Describe(t.Result, DateTimeOffset.UtcNow),
                TaskScheduler.FromCurrentSynchronizationContext());
    }

    // ---- Back up now -----------------------------------------------------------------------------------

    private async void BackUpNow(Button button)
    {
        button.IsEnabled = false;
        button.Content = "Backing up…";
        var path = _dbPath;
        try
        {
            await Task.Run(() => AutoBackup.Now(path, DateTimeOffset.UtcNow, DefaultFolder, Version));
            button.Content = "Done ✓";
            RefreshLastBackup();
            await Task.Delay(2500);
        }
        catch (Exception ex) when (ex is IOException or Microsoft.Data.Sqlite.SqliteException or UnauthorizedAccessException)
        {
            button.Content = "Failed";
            if (_backupDesc is not null) _backupDesc.Text = "Backup failed: " + ex.Message;
            await Task.Delay(2500);
        }
        button.Content = "Back up";
        button.IsEnabled = true;
    }

    // ---- Restore ---------------------------------------------------------------------------------------

    private async void ChooseRestoreFile()
    {
        var dlg = new OpenFileDialog { Title = "Restore from backup", Filter = "TimeSink backup (*.timesink)|*.timesink|All files|*.*", InitialDirectory = Directory.Exists(DefaultFolder) ? DefaultFolder : "" };
        if (NativeDialog(() => dlg.ShowDialog(Window.GetWindow(this)) == true) is not true) return;
        var file = dlg.FileName;

        BackupInspection info;
        try { info = await Task.Run(() => RestoreService.Inspect(file)); }
        catch (Exception ex) when (ex is BackupException or Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            ShowDialog("Cannot restore", ex.Message, null, ("OK", CloseDialog, false, false));
            return;
        }

        var made = info.Manifest.CreatedUtc.ToLocalTime().ToString("d MMM yyyy HH:mm");
        var detail = $"{Path.GetFileName(file)}\nMade {made} · {info.Apps} apps · {info.Sessions:N0} sessions" +
                     (info.NeedsMigration ? "\nThis backup is from an older version and will be upgraded first." : "");
        var body = "Replace swaps your current history for the backup (a safety copy of the current data is saved first). " +
                   "Merge adds the backup's apps and sessions to your current history; merging the same file twice changes nothing.";
        ShowDialog("Restore from backup", detail + "\n\n" + body, null,
            ("Cancel", CloseDialog, false, false),
            ("Merge", () => RunRestore(file, replace: false), false, false),
            ("Replace", () => RunRestore(file, replace: true), true, false));
    }

    private async void RunRestore(string file, bool replace)
    {
        ShowDialog(replace ? "Replacing…" : "Merging…", "Restoring your history. This takes a moment.", null);
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
                    return "Your history was replaced." + (r.SafetyCopy is null ? "" : "\nSafety copy: " + Path.GetFileName(r.SafetyCopy));
                }
                var m = RestoreService.Merge(file, path, zone);
                return $"Merged {m.SessionsAdded:N0} new sessions and {m.AppsAdded} new apps ({m.SessionsSkipped:N0} sessions were already there).";
            });
        }
        catch (Exception ex) when (ex is BackupException or Microsoft.Data.Sqlite.SqliteException or IOException)
        {
            summary = "Restore failed: " + ex.Message;
        }
        ShowDialog(replace ? "Replace finished" : "Merge finished", summary, null, ("OK", CloseDialog, true, false));
        RefreshInfo();
    }

    // ---- Export ----------------------------------------------------------------------------------------

    private void ChooseExport() =>
        ShowDialog("Export all data", "CSV writes two files (sessions and daily totals) into a folder you choose. JSON writes one file with everything.", null,
            ("Cancel", CloseDialog, false, false),
            ("JSON", () => RunExport(csv: false), false, false),
            ("CSV", () => RunExport(csv: true), true, false));

    private async void RunExport(bool csv)
    {
        CloseDialog();
        var path = _dbPath;
        string? result = null;
        try
        {
            if (csv)
            {
                var dlg = new OpenFolderDialog { Title = "Export CSV into folder" };
                if (NativeDialog(() => dlg.ShowDialog(Window.GetWindow(this)) == true) is not true) return;
                var folder = dlg.FolderName;
                var files = await Task.Run(() => DataExport.ExportCsv(path, folder));
                result = $"Saved {Path.GetFileName(files.SessionsCsv)} and {Path.GetFileName(files.DailyCsv)} in\n{folder}";
            }
            else
            {
                var dlg = new SaveFileDialog { Title = "Export JSON", FileName = "timesink-export.json", Filter = "JSON (*.json)|*.json" };
                if (NativeDialog(() => dlg.ShowDialog(Window.GetWindow(this)) == true) is not true) return;
                var file = dlg.FileName;
                await Task.Run(() => DataExport.ExportJson(path, file));
                result = "Saved " + file;
            }
        }
        catch (Exception ex) when (ex is IOException or Microsoft.Data.Sqlite.SqliteException or UnauthorizedAccessException)
        {
            result = "Export failed: " + ex.Message;
        }
        ShowDialog("Export", result, null, ("OK", CloseDialog, true, false));
    }

    // ---- Delete all history ----------------------------------------------------------------------------

    private void ConfirmDelete()
    {
        var box = new TextBox { Margin = new Thickness(0, 10, 0, 0), Padding = new Thickness(6, 4, 6, 4) };
        box.SetResourceReference(Control.BackgroundProperty, "Brush.SurfaceAlt");
        box.SetResourceReference(Control.ForegroundProperty, "Brush.Text");
        box.SetResourceReference(Control.BorderBrushProperty, "Brush.Border");
        var buttons = ShowDialog("Delete all history",
            "This removes every recorded session and the usage totals built from them. Your app list and settings stay. This cannot be undone.\n\nType DELETE to confirm.",
            box, ("Cancel", CloseDialog, false, false), ("Delete everything", RunDelete, false, true));
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
                result = "All history was deleted.";
            }
            catch (Exception ex) when (ex is ArgumentException or Microsoft.Data.Sqlite.SqliteException or IOException)
            {
                result = "Delete failed: " + ex.Message;
            }
            ShowDialog("Delete all history", result, null, ("OK", CloseDialog, true, false));
            RefreshInfo();
        }
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

        _overlay = new Grid { Background = new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)) };
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
