namespace TimeSink.Core.Apps;

/// <summary>What a <see cref="MergeCandidate"/> is today: tracked, already merged into the Main app, stopped, or merely installed.</summary>
public enum CandidateKind { Tracked, Merged, Retired, Installed }

/// <summary>
/// An app the merge picker offers for folding into a Main app. <see cref="AppId"/> is 0 for an installed app that is not
/// in the database yet; <see cref="Installed"/> is set only for those. <see cref="Seconds"/> is the running time of a
/// tracked or merged app. <see cref="Png"/> is the stored icon (or one the caller already knew for an installed app).
/// </summary>
public sealed record MergeCandidate(string Key, CandidateKind Kind, string Name, string ExePath, long AppId, long Seconds,
    InstalledApp? Installed, byte[]? Png)
{
    /// <summary>Tracked or merged: the apps the tracked filter lists. Retired and installed ones are "untracked".</summary>
    public bool IsTracked => Kind is CandidateKind.Tracked or CandidateKind.Merged;
}

/// <summary>How many Candidates a confirm would add and remove, and whether it would start tracking apps that are not tracked now.</summary>
public readonly record struct MergeSummary(int Adds, int Removes, bool StartsTracking)
{
    public bool Changed => Adds + Removes > 0;
}

/// <summary>
/// A merge opened for one Main app (<see cref="AppCatalogue.OpenMergePlan"/>): the Candidates, which of them are ticked,
/// and <see cref="Confirm"/>. Already Merged apps start ticked, so unticking one unmerges it. A snapshot: it does not
/// follow later changes to the database.
/// </summary>
public sealed class MergePlan
{
    private readonly AppCatalogue _catalogue;
    private readonly string _dbPath;
    private readonly IDesktop _desktop;
    private readonly HashSet<string> _ticked = new(StringComparer.Ordinal);

    internal MergePlan(AppCatalogue catalogue, string dbPath, IDesktop desktop, long mainId, string mainName,
        IReadOnlyList<MergeCandidate> candidates)
    {
        _catalogue = catalogue;
        _dbPath = dbPath;
        _desktop = desktop;
        MainId = mainId;
        MainName = mainName;
        Candidates = candidates;
        foreach (var c in candidates.Where(c => c.Kind == CandidateKind.Merged)) _ticked.Add(c.Key);
    }

    public long MainId { get; }
    public string MainName { get; }

    /// <summary>Every Candidate, sorted by name.</summary>
    public IReadOnlyList<MergeCandidate> Candidates { get; }

    public bool IsTicked(string key) => _ticked.Contains(key);

    /// <summary>Ticks the Candidate, or unticks it when it was ticked. An unknown key does nothing.</summary>
    public void Toggle(string key)
    {
        if (Candidates.All(c => c.Key != key)) return;
        if (!_ticked.Remove(key)) _ticked.Add(key);
    }

    /// <summary>Ticked Candidates that would be merged in, unticked Merged apps that would be unmerged, and whether any ticked one is not tracked now.</summary>
    public MergeSummary Summary => new(
        Candidates.Count(c => c.Kind != CandidateKind.Merged && IsTicked(c.Key)),
        Candidates.Count(c => c.Kind == CandidateKind.Merged && !IsTicked(c.Key)),
        Candidates.Any(c => !c.IsTracked && IsTicked(c.Key)));

    /// <summary>
    /// Unmerges every unticked Merged app and folds every ticked Candidate into the Main app, in Candidate order: a
    /// Retired app is restored first (unless it is already tracked again), an installed one is added first (its icon is
    /// the known one, else extracted through the desktop seam; an <see cref="IOException"/> there only means no icon).
    /// Apps added this way count towards "Added N".
    /// There is no transaction around the whole confirm: each step commits on its own. On
    /// <see cref="CatalogueOutcome.Unavailable"/> the steps before the failure stay in the database, the rest did not
    /// run, and the plan is unchanged (same ticks), so confirming again finishes the job without repeating those steps.
    /// </summary>
    public CatalogueOutcome Confirm()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        try
        {
            using var lib = AppLibrary.Open(_dbPath);
            HashSet<long>? tracked = null; // ids tracked when the confirm started, read once and only if a stopped app is ticked
            foreach (var c in Candidates)
            {
                var ticked = IsTicked(c.Key);
                if (c.Kind == CandidateKind.Merged)
                {
                    if (!ticked) lib.Unmerge(c.AppId);
                    continue;
                }
                if (!ticked) continue;

                long id;
                if (c.Kind == CandidateKind.Tracked) id = c.AppId;
                else if (c.Kind == CandidateKind.Retired)
                {
                    // a repeat after a partial failure finds it tracked (perhaps merged already): restoring again would reset its start
                    tracked ??= lib.Tracked().SelectMany(t => t.Merged.Select(m => m.Id).Append(t.Id)).ToHashSet();
                    if (!tracked.Contains(c.AppId)) lib.Restore(c.AppId, now);
                    id = c.AppId;
                }
                else
                {
                    var app = c.Installed!;
                    var png = c.Png ?? ExtractIcon(app);
                    id = lib.AddTracked(app.ToNewApp(), now, png).AppId;
                }
                if (!lib.Merge(id, MainId)) continue;
                if (!c.IsTracked) _catalogue.NoteAddedElsewhere(c.ExePath);
            }
        }
        catch (Exception e) when (AppCatalogue.IsBusyOrMissing(e)) { return CatalogueOutcome.Unavailable; }
        return CatalogueOutcome.Done;
    }

    // a file that cannot be read is no reason to fail the confirm (or to call the database busy): the app just has no icon
    private byte[]? ExtractIcon(InstalledApp app)
    {
        try { return _desktop.ExtractIcon(app.ExePath, app.Aumid, app.IconFile, 64); }
        catch (IOException) { return null; }
    }
}
