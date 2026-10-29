using Microsoft.Data.Sqlite;

namespace TimeSink.Core.Apps;

/// <summary>
/// Result of an operation that touches the database: done, the database was busy or missing, or (for
/// <see cref="AppCatalogue.ApplyChange"/> only) the change did not apply.
/// </summary>
public enum CatalogueOutcome { Done, Unavailable, Rejected }

/// <summary>The tracked apps (<see cref="AppCatalogue.Tracked"/>) and their stored icons by app id; empty when <see cref="Outcome"/> is Unavailable.</summary>
public sealed record TrackedSnapshot(CatalogueOutcome Outcome, IReadOnlyList<TrackedEntry> Apps, IReadOnlyDictionary<long, byte[]?> Icons);

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
    private readonly object _scanRun = new(); // held for a whole scan, so callers share it; never taken by LastInstalled or NewVisit
    private readonly Dictionary<string, RunningCandidate> _trackedThisVisit = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _addedElsewhere = new(StringComparer.OrdinalIgnoreCase);
    private readonly AppIconSource _icons;
    private IReadOnlyList<InstalledApp>? _installed; // the slow scan, taken lazily once per visit (under _gate)
    private IReadOnlyList<InstalledApp>? _lastInstalled; // survives NewVisit, so a reopened screen can draw at once (under _gate)
    private Dictionary<string, InstalledApp>? _lastInstalledByPath; // _lastInstalled by exe path, built with it (under _gate)
    private int _visit; // bumped by NewVisit, so a scan that began in an earlier visit does not count for this one (under _gate)

    public AppCatalogue(string dbPath, IDesktop desktop, AppIconSource icons)
    {
        _dbPath = dbPath;
        _desktop = desktop;
        _icons = icons;
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
            var icon = _icons.ForPath(app.ExePath, aumid, null, iconPx, cachedOnly: budget <= 0, out var extracted);
            if (extracted) budget--;
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
            return ReadLibrary(lib);
        }
        catch (Exception e) when (IsBusyOrMissing(e)) { return new LibraryState(CatalogueOutcome.Unavailable, [], [], new HashSet<long>()); }
    }

    private static LibraryState ReadLibrary(AppLibrary lib) =>
        new(CatalogueOutcome.Done, lib.Library(), lib.MergedAliases(), lib.TrivialRetired());

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

    /// <summary>
    /// A snapshot of the tracked (Main) apps with their Merged apps, read now, plus the stored icon of each one whose id
    /// is not in <paramref name="iconsKnown"/> (null where none is stored). Empty with
    /// <see cref="CatalogueOutcome.Unavailable"/> when the database is busy or missing.
    /// </summary>
    public TrackedSnapshot Tracked(IReadOnlySet<long>? iconsKnown = null)
    {
        try
        {
            using var lib = AppLibrary.Open(_dbPath);
            var apps = lib.Tracked();
            var wanted = apps.Where(a => iconsKnown is null || !iconsKnown.Contains(a.Id)).Select(a => a.Id).ToList();
            var stored = _icons.Stored(wanted);
            var icons = new Dictionary<long, byte[]?>();
            foreach (var id in wanted) icons[id] = stored.GetValueOrDefault(id);
            return new TrackedSnapshot(CatalogueOutcome.Done, apps, icons);
        }
        catch (Exception e) when (IsBusyOrMissing(e)) { return new TrackedSnapshot(CatalogueOutcome.Unavailable, [], new Dictionary<long, byte[]?>()); }
    }

    /// <summary>The stored icon of any app (a Merged one too), or null when none is stored or the database is busy or missing.</summary>
    public byte[]? StoredIcon(long appId) => _icons.Stored([appId]).GetValueOrDefault(appId);

    /// <summary>
    /// Applies one edit to an app. <see cref="CatalogueOutcome.Rejected"/> when the change does not apply (a blank name, a
    /// Rename of an unknown app, a Make-main on an app that is not Merged) and <see cref="CatalogueOutcome.Unavailable"/>
    /// when the database is busy or missing; in both cases nothing changed. The other kinds do not check that the app
    /// exists: on an unknown app they change nothing and still report Done. A Retire or Restore also updates the "Added N" count.
    /// </summary>
    public CatalogueOutcome ApplyChange(AppChange change)
    {
        try
        {
            using var lib = AppLibrary.Open(_dbPath);
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            switch (change.Kind)
            {
                case ChangeKind.Rename: if (!lib.Rename(change.AppId, change.Name)) return CatalogueOutcome.Rejected; break;
                case ChangeKind.SetKind: lib.SetKind(change.AppId, change.GameKind); break;
                case ChangeKind.SetBackground: lib.SetBackground(change.AppId, change.Background); break;
                case ChangeKind.Retire: lib.Retire(change.AppId, now); break;
                case ChangeKind.Restore: lib.Restore(change.AppId, now); break;
                case ChangeKind.MakeMain: if (!lib.MakeMain(change.AppId)) return CatalogueOutcome.Rejected; break;
                case ChangeKind.Unmerge: lib.Unmerge(change.AppId); break;
                case ChangeKind.ClearHistory: lib.ClearHistory(change.AppId); break;
            }
        }
        catch (Exception e) when (IsBusyOrMissing(e)) { return CatalogueOutcome.Unavailable; }
        if (change.Kind == ChangeKind.Retire) foreach (var path in change.ExePaths) NoteStopped(path);
        else if (change.Kind == ChangeKind.Restore) foreach (var path in change.ExePaths) NoteAddedElsewhere(path);
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
            _installed = null;
            _visit++;
        }
        _icons.NewVisit();
    }

    /// <summary>
    /// Opens a merge for the Main app <paramref name="mainId"/>: the other Tracked apps, the apps already Merged into it
    /// (ticked), Retired apps with real history (not trivial ones, not Windows' own processes) and installed apps not
    /// yet known. Null when that app is not a Tracked Main app or the database is busy or missing. The first call of a
    /// visit runs the installed-apps scan, so call from a pool thread. Installed apps carry the icon already extracted
    /// at <paramref name="iconPx"/> (see <see cref="AppIconSource.ForPath"/>), or none; nothing is extracted here.
    /// </summary>
    public MergePlan? OpenMergePlan(long mainId, int iconPx = 256)
    {
        TrackedEntry main;
        var list = new List<MergeCandidate>();
        LibraryState state;
        try
        {
            using var lib = AppLibrary.Open(_dbPath);
            var tracked = lib.Tracked();
            if (tracked.FirstOrDefault(a => a.Id == mainId) is not { } found) return null;
            main = found;
            foreach (var a in tracked.Where(a => a.Id != mainId))
                list.Add(new MergeCandidate("t" + a.Id, CandidateKind.Tracked, a.DisplayName, a.ExePath, a.Id, a.Seconds, null, null));
            foreach (var m in main.Merged)
                list.Add(new MergeCandidate("m" + m.Id, CandidateKind.Merged, m.DisplayName, m.ExePath, m.Id, m.Seconds, null, null));
            state = ReadLibrary(lib);
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows) + Path.DirectorySeparatorChar;
            foreach (var r in state.Retired.Where(a => !a.ExePath.StartsWith(windows, StringComparison.OrdinalIgnoreCase)))
                list.Add(new MergeCandidate("s" + r.Id, CandidateKind.Retired, r.DisplayName, r.ExePath, r.Id, 0, null, null));
            var icons = _icons.Stored(list.Select(c => c.AppId));
            for (var i = 0; i < list.Count; i++) list[i] = list[i] with { Png = icons.GetValueOrDefault(list[i].AppId) };
        }
        catch (Exception e) when (IsBusyOrMissing(e)) { return null; }

        foreach (var i in Installed())
        {
            if (state.StateOf(i.ExePath, i.PackageFamily) is not null) continue; // already tracked, stopped or merged
            var key = "i" + i.ExePath.ToLowerInvariant();
            var png = _icons.ForPath(i.ExePath, i.Aumid, i.IconFile, iconPx, cachedOnly: true, out _);
            list.Add(new MergeCandidate(key, CandidateKind.Installed, i.Name, i.ExePath, 0, 0, i, png));
        }
        var sorted = list.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        return new MergePlan(this, _dbPath, _icons, main.Id, main.DisplayName, sorted);
    }

    /// <summary>
    /// Every installed app, from the one scan of this visit (shared with the merge plan). The first call of a visit
    /// runs the scan, so call from a pool thread. An empty scan (it failed) keeps the list of the last good one.
    /// </summary>
    public IReadOnlyList<InstalledApp> Installed()
    {
        lock (_gate)
        {
            if (_installed is not null) return _installed;
        }
        lock (_scanRun) // one scan at a time; the fields are only ever held under the short _gate
        {
            int visit;
            lock (_gate)
            {
                if (_installed is not null) return _installed; // another caller scanned while this one waited
                visit = _visit;
            }
            var scan = _desktop.ScanInstalled();
            lock (_gate)
            {
                if (scan.Count == 0 && _lastInstalled is { Count: > 0 } last) scan = last;
                _lastInstalled = scan;
                _lastInstalledByPath = scan.GroupBy(i => i.ExePath, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                if (visit == _visit) _installed = scan; // a NewVisit meanwhile: the next caller scans afresh
            }
            return scan;
        }
    }

    /// <summary>The newest scan, possibly from an earlier visit, or null before the first one; never scans.</summary>
    public IReadOnlyList<InstalledApp>? LastInstalled
    {
        get { lock (_gate) return _lastInstalled; }
    }

    /// <summary>The installed app with this exe path (case ignored) in the newest scan, or null before the first scan or when it is not in it; never scans.</summary>
    public InstalledApp? InstalledBy(string exePath)
    {
        lock (_gate) return _lastInstalledByPath?.GetValueOrDefault(exePath);
    }

    /// <summary>True for the failures that mean a busy or missing database (the only ones the catalogue turns into <see cref="CatalogueOutcome.Unavailable"/>).</summary>
    internal static bool IsBusyOrMissing(Exception e) => e is SqliteException or IOException;
}
