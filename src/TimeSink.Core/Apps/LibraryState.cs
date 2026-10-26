namespace TimeSink.Core.Apps;

/// <summary>
/// A snapshot of which apps are tracked or Retired (<see cref="AppCatalogue.Library"/>). Empty, with
/// <see cref="CatalogueOutcome.Unavailable"/>, when the database was busy or missing. It does not follow later changes.
/// </summary>
public sealed class LibraryState
{
    private readonly IReadOnlyList<LibraryApp> _library;
    private readonly IReadOnlyList<MergedAlias> _aliases;

    internal LibraryState(CatalogueOutcome outcome, IReadOnlyList<LibraryApp> library, IReadOnlyList<MergedAlias> aliases,
        IReadOnlySet<long> trivialRetired)
    {
        Outcome = outcome;
        _library = library;
        _aliases = aliases;
        Retired = library.Where(a => a.State == "retired" && !trivialRetired.Contains(a.Id)).OrderByDescending(a => a.RetiredUtc).ToList();
    }

    public CatalogueOutcome Outcome { get; }

    /// <summary>Stopped apps with real history (not trivial ones), most recently stopped first: restorable whether or not they are installed.</summary>
    public IReadOnlyList<LibraryApp> Retired { get; }

    /// <summary>
    /// The library app an installed or running app counts as: tracked or Retired, matched by exe path, then by package
    /// family (a tracked app only). A Merged app counts under the app it is merged into. Null when unknown.
    /// </summary>
    public LibraryApp? StateOf(string exePath, string? family)
    {
        static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        LibraryApp? KeeperOf(MergedAlias? alias) => alias is null ? null : _library.FirstOrDefault(a => a.Id == alias.CanonicalId);

        return _library.FirstOrDefault(a => Same(a.ExePath, exePath))
            ?? KeeperOf(_aliases.FirstOrDefault(m => Same(m.ExePath, exePath)))
            ?? (family is null ? null : _library.FirstOrDefault(a => a.State == "tracked" && Same(a.PackageFamily, family)))
            ?? (family is null ? null : KeeperOf(_aliases.FirstOrDefault(m => Same(m.PackageFamily, family))));
    }
}
