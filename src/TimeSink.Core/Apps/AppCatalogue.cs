using Microsoft.Data.Sqlite;

namespace TimeSink.Core.Apps;

/// <summary>Result of an operation that touches the database: done, or the database was busy or missing.</summary>
public enum CatalogueOutcome { Done, Unavailable }

/// <summary>
/// A running app the user may track. <see cref="Tracking"/> marks a row tracked earlier in this visit, which stays
/// on screen. <see cref="Png"/> is the icon when one was extracted.
/// </summary>
public sealed record RunningCandidate(RunningApp App, string? Aumid, byte[]? Png, bool Tracking);

/// <summary>The Running Candidates (longest open first), or <see cref="CatalogueOutcome.Unavailable"/> with none.</summary>
public sealed record RunningList(CatalogueOutcome Outcome, IReadOnlyList<RunningCandidate> Candidates);

/// <summary>
/// The Add-app workflow behind one small interface: Running Candidates, track and dismiss, and the per-visit state
/// (rows tracked this visit, apps added elsewhere, the "Added N" count). <see cref="AppLibrary"/> stays beneath it as
/// the SQLite adapter and <see cref="IDesktop"/> is the seam to the desktop. Only <see cref="SqliteException"/> and
/// <see cref="IOException"/> count as a busy or missing database; anything else propagates. Thread-safe, so the
/// capture can run on a pool thread while the UI thread tracks.
/// </summary>
public sealed class AppCatalogue
{
    private readonly string _dbPath;
    private readonly IDesktop _desktop;
    private readonly object _gate = new();
    private readonly Dictionary<string, RunningCandidate> _trackedThisVisit = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _addedElsewhere = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]?> _icons = new(StringComparer.OrdinalIgnoreCase);

    public AppCatalogue(string dbPath, IDesktop desktop)
    {
        _dbPath = dbPath;
        _desktop = desktop;
    }

    /// <summary>Apps added in this visit, from the Running tab or elsewhere, each counted once.</summary>
    public int AddedCount
    {
        get { lock (_gate) return _trackedThisVisit.Keys.Union(_addedElsewhere, StringComparer.OrdinalIgnoreCase).Count(); }
    }

    /// <summary>
    /// Untracked, un-ignored running apps plus the rows tracked this visit (marked <see cref="RunningCandidate.Tracking"/>),
    /// by start time. Icons are extracted at <paramref name="iconPx"/> once per app, for the first
    /// <paramref name="iconLimit"/> candidates not yet cached.
    /// </summary>
    public RunningList Running(int iconPx = 256, int iconLimit = int.MaxValue)
    {
        ExcludedApps excluded;
        try
        {
            using var lib = AppLibrary.Open(_dbPath);
            excluded = lib.ExcludedFromRunning();
        }
        catch (Exception e) when (IsBusyOrMissing(e)) { return new RunningList(CatalogueOutcome.Unavailable, []); }

        var capture = _desktop.CaptureWindows();
        var apps = RunningApps.Select(capture.Windows, excluded);

        var budget = iconLimit;
        var fresh = new List<RunningCandidate>();
        foreach (var app in apps)
        {
            var aumid = capture.AumidByPath.GetValueOrDefault(app.ExePath);
            bool known;
            lock (_gate) known = _icons.ContainsKey(app.ExePath);
            if (!known && budget > 0)
            {
                budget--;
                var png = _desktop.ExtractIcon(app.ExePath, aumid, null, iconPx);
                lock (_gate) _icons[app.ExePath] = png;
            }
            byte[]? icon;
            lock (_gate) icon = _icons.GetValueOrDefault(app.ExePath);
            fresh.Add(new RunningCandidate(app, aumid, icon, false));
        }

        lock (_gate)
        {
            var all = fresh.Where(c => !_trackedThisVisit.ContainsKey(c.App.ExePath)).Concat(_trackedThisVisit.Values)
                .OrderBy(c => c.App.StartUtc).ToList();
            return new RunningList(CatalogueOutcome.Done, all);
        }
    }

    /// <summary>Starts tracking the app. On <see cref="CatalogueOutcome.Unavailable"/> nothing changed, so the caller can offer a retry.</summary>
    public CatalogueOutcome Track(RunningCandidate candidate)
    {
        var app = candidate.App;
        try
        {
            using var lib = AppLibrary.Open(_dbPath);
            lib.AddTracked(new NewApp(app.ExePath, app.DisplayName, app.PackageFamily), DateTimeOffset.UtcNow.ToUnixTimeSeconds(), candidate.Png);
        }
        catch (Exception e) when (IsBusyOrMissing(e)) { return CatalogueOutcome.Unavailable; }
        lock (_gate) _trackedThisVisit[app.ExePath] = candidate with { Tracking = true };
        return CatalogueOutcome.Done;
    }

    /// <summary>"Not this one": the app joins the ignore list and is no longer offered.</summary>
    public CatalogueOutcome Dismiss(RunningCandidate candidate)
    {
        try
        {
            using var lib = AppLibrary.Open(_dbPath);
            lib.Ignore(candidate.App.ExePath, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
        catch (Exception e) when (IsBusyOrMissing(e)) { return CatalogueOutcome.Unavailable; }
        return CatalogueOutcome.Done;
    }

    /// <summary>An app was added from the Installed, Browse or merge views: it counts towards "Added N".</summary>
    public void NoteAddedElsewhere(string exePath)
    {
        lock (_gate) _addedElsewhere.Add(exePath);
    }

    /// <summary>An app was stopped (retired) in this visit: it no longer counts as added and its "Tracking" row goes.</summary>
    public void NoteStopped(string exePath)
    {
        lock (_gate)
        {
            _addedElsewhere.Remove(exePath);
            _trackedThisVisit.Remove(exePath);
        }
    }

    /// <summary>Starts a fresh visit: forgets what was added in the last one. Cached icons are kept.</summary>
    public void NewVisit()
    {
        lock (_gate)
        {
            _trackedThisVisit.Clear();
            _addedElsewhere.Clear();
        }
    }

    private static bool IsBusyOrMissing(Exception e) => e is SqliteException or IOException;
}
