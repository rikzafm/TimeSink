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
    private readonly object _scanGate = new();
    private readonly Dictionary<string, RunningCandidate> _trackedThisVisit = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _addedElsewhere = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]?> _icons = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<InstalledApp>? _installed; // the slow scan, taken lazily once per visit
    private IReadOnlyList<InstalledApp>? _lastInstalled; // survives NewVisit, so a reopened screen can draw at once

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

    /// <summary>A snapshot of the tracked and Retired apps, read now. Empty with <see cref="CatalogueOutcome.Unavailable"/> when the database is busy or missing.</summary>
    public LibraryState Library()
    {
        try
        {
            using var lib = AppLibrary.Open(_dbPath);
            return new LibraryState(CatalogueOutcome.Done, lib.Library(), lib.MergedAliases(), lib.TrivialRetired());
        }
        catch (Exception e) when (IsBusyOrMissing(e)) { return new LibraryState(CatalogueOutcome.Unavailable, [], [], new HashSet<long>()); }
    }

    /// <summary>
    /// Starts tracking an app that is not running (picked from the Installed list or a file): it counts towards "Added N".
    /// <paramref name="iconPng"/> is stored as its icon. On <see cref="CatalogueOutcome.Unavailable"/> nothing changed.
    /// </summary>
    public CatalogueOutcome AddApp(NewApp app, byte[]? iconPng)
    {
        try
        {
            using var lib = AppLibrary.Open(_dbPath);
            lib.AddTracked(app, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), iconPng);
        }
        catch (Exception e) when (IsBusyOrMissing(e)) { return CatalogueOutcome.Unavailable; }
        NoteAddedElsewhere(app.ExePath);
        return CatalogueOutcome.Done;
    }

    /// <summary>Stops tracking the app, keeping its history; it no longer counts as added in this visit.</summary>
    public CatalogueOutcome Retire(LibraryApp app)
    {
        try
        {
            using var lib = AppLibrary.Open(_dbPath);
            lib.Retire(app.Id, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
        catch (Exception e) when (IsBusyOrMissing(e)) { return CatalogueOutcome.Unavailable; }
        NoteStopped(app.ExePath);
        return CatalogueOutcome.Done;
    }

    /// <summary>Tracks a Retired app again; it counts towards "Added N".</summary>
    public CatalogueOutcome Restore(LibraryApp app)
    {
        try
        {
            using var lib = AppLibrary.Open(_dbPath);
            lib.Restore(app.Id, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        }
        catch (Exception e) when (IsBusyOrMissing(e)) { return CatalogueOutcome.Unavailable; }
        NoteAddedElsewhere(app.ExePath);
        return CatalogueOutcome.Done;
    }

    /// <summary>Sets what Year > Wrapped counts the app as.</summary>
    public CatalogueOutcome SetKind(long appId, AppKind kind) => Apply(lib => lib.SetKind(appId, kind));

    /// <summary>Sets whether the app ranks after the others.</summary>
    public CatalogueOutcome SetBackground(long appId, bool background) => Apply(lib => lib.SetBackground(appId, background));

    private CatalogueOutcome Apply(Action<AppLibrary> change)
    {
        try
        {
            using var lib = AppLibrary.Open(_dbPath);
            change(lib);
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
        lock (_scanGate) _installed = null;
    }

    /// <summary>
    /// Opens a merge for the Main app <paramref name="mainId"/>: the other Tracked apps, the apps already Merged into it
    /// (ticked), Retired apps with real history (not trivial ones, not Windows' own processes) and installed apps not
    /// yet known. Null when that app is not a Tracked Main app or the database is busy or missing. The first call of a
    /// visit runs the installed-apps scan, so call from a pool thread. <paramref name="knownIcons"/> (by exe path)
    /// supplies icons already extracted for installed apps.
    /// </summary>
    public MergePlan? OpenMergePlan(long mainId, IReadOnlyDictionary<string, byte[]?>? knownIcons = null)
    {
        TrackedEntry main;
        var list = new List<MergeCandidate>();
        IReadOnlyList<LibraryApp> library;
        IReadOnlyList<MergedAlias> aliases;
        try
        {
            using var lib = AppLibrary.Open(_dbPath);
            var tracked = lib.Tracked();
            if (tracked.FirstOrDefault(a => a.Id == mainId) is not { } found) return null;
            main = found;
            foreach (var a in tracked.Where(a => a.Id != mainId))
                list.Add(new MergeCandidate("t" + a.Id, CandidateKind.Tracked, a.DisplayName, a.ExePath, a.Id, a.Seconds, null, lib.GetIcon(a.Id)));
            foreach (var m in main.Merged)
                list.Add(new MergeCandidate("m" + m.Id, CandidateKind.Merged, m.DisplayName, m.ExePath, m.Id, m.Seconds, null, lib.GetIcon(m.Id)));
            library = lib.Library();
            aliases = lib.MergedAliases();
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + Path.DirectorySeparatorChar;
            var trivial = lib.TrivialRetired();
            foreach (var r in library.Where(a => a.State == "retired" && !trivial.Contains(a.Id) && !a.ExePath.StartsWith(windows, StringComparison.OrdinalIgnoreCase)))
                list.Add(new MergeCandidate("s" + r.Id, CandidateKind.Retired, r.DisplayName, r.ExePath, r.Id, 0, null, lib.GetIcon(r.Id)));
        }
        catch (Exception e) when (IsBusyOrMissing(e)) { return null; }

        foreach (var i in Installed())
        {
            if (IsKnown(i, library, aliases)) continue; // already tracked, stopped or merged
            var key = "i" + i.ExePath.ToLowerInvariant();
            list.Add(new MergeCandidate(key, CandidateKind.Installed, i.Name, i.ExePath, 0, 0, i, knownIcons?.GetValueOrDefault(i.ExePath)));
        }
        var sorted = list.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        return new MergePlan(this, _dbPath, _desktop, main.Id, main.DisplayName, sorted);
    }

    /// <summary>
    /// Every installed app, from the one scan of this visit (shared with the merge plan). The first call of a visit
    /// runs the scan, so call from a pool thread. An empty scan (it failed) keeps the list of the last good one.
    /// </summary>
    public IReadOnlyList<InstalledApp> Installed()
    {
        lock (_scanGate)
        {
            if (_installed is not null) return _installed;
            var scan = _desktop.ScanInstalled();
            if (scan.Count == 0 && _lastInstalled is { Count: > 0 } last) scan = last;
            return _installed = _lastInstalled = scan;
        }
    }

    /// <summary>The newest scan, possibly from an earlier visit, or null before the first one; never scans.</summary>
    public IReadOnlyList<InstalledApp>? LastInstalled
    {
        get { lock (_scanGate) return _lastInstalled; }
    }

    // a merged app is the app it counts under, so it reads as tracked (or stopped) with that app
    private static bool IsKnown(InstalledApp i, IReadOnlyList<LibraryApp> library, IReadOnlyList<MergedAlias> aliases)
    {
        bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        if (library.Any(a => Same(a.ExePath, i.ExePath))) return true;
        if (aliases.Any(m => Same(m.ExePath, i.ExePath))) return true;
        if (i.PackageFamily is null) return false;
        return library.Any(a => a.State == "tracked" && Same(a.PackageFamily, i.PackageFamily))
            || aliases.Any(m => Same(m.PackageFamily, i.PackageFamily));
    }

    private static bool IsBusyOrMissing(Exception e) => MergePlan.IsBusyOrMissing(e);
}
