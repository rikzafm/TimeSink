namespace TimeSink.Core.Apps;

/// <summary>What an <see cref="AppChange"/> does to an app.</summary>
public enum ChangeKind { Rename, SetKind, SetBackground, Retire, Restore, MakeMain, Unmerge, ClearHistory, SetIcon }

/// <summary>
/// One edit to an app in the library, built with the factory for its <see cref="ChangeKind"/> and applied with
/// <see cref="AppCatalogue.ApplyChange"/>.
/// </summary>
public sealed class AppChange
{
    private AppChange(ChangeKind kind, long appId) { Kind = kind; AppId = appId; }

    public ChangeKind Kind { get; }
    internal long AppId { get; }
    internal string Name { get; private init; } = "";
    internal AppKind GameKind { get; private init; }
    internal bool Background { get; private init; }
    internal byte[] Icon { get; private init; } = [];
    internal IReadOnlyList<string> ExePaths { get; private init; } = [];

    /// <summary>Changes the name shown everywhere. A blank name (or an unknown app) is <see cref="CatalogueOutcome.Rejected"/>.</summary>
    public static AppChange Rename(long appId, string name) => new(ChangeKind.Rename, appId) { Name = name };

    /// <summary>Sets what Year > Wrapped counts the app as.</summary>
    public static AppChange SetKind(long appId, AppKind kind) => new(ChangeKind.SetKind, appId) { GameKind = kind };

    /// <summary>Sets whether the app ranks after the others.</summary>
    public static AppChange SetBackground(long appId, bool background) => new(ChangeKind.SetBackground, appId) { Background = background };

    /// <summary>Stops tracking the app, keeping its history; it no longer counts as added in this visit.</summary>
    public static AppChange Retire(LibraryApp app) => new(ChangeKind.Retire, app.Id) { ExePaths = [app.ExePath] };

    /// <summary>Stops tracking the app and, for this visit's count, every app merged into it.</summary>
    public static AppChange Retire(TrackedEntry app) =>
        new(ChangeKind.Retire, app.Id) { ExePaths = app.Merged.Select(m => m.ExePath).Append(app.ExePath).ToList() };

    /// <summary>Tracks a Retired app again; it counts towards "Added N".</summary>
    public static AppChange Restore(LibraryApp app) => new(ChangeKind.Restore, app.Id) { ExePaths = [app.ExePath] };

    /// <summary>Makes a Merged app the Main app of its group. Rejected when it is not a tracked Merged app.</summary>
    public static AppChange MakeMain(long mergedAppId) => new(ChangeKind.MakeMain, mergedAppId);

    /// <summary>Makes a Merged app a separate tracked app again.</summary>
    public static AppChange Unmerge(long mergedAppId) => new(ChangeKind.Unmerge, mergedAppId);

    /// <summary>Stores <paramref name="png"/> as the app's icon (decision 152).</summary>
    public static AppChange SetIcon(long appId, byte[] png) => new(ChangeKind.SetIcon, appId) { Icon = png };

    /// <summary>Deletes the recorded time of the app and of everything merged into it.</summary>
    public static AppChange ClearHistory(long appId) => new(ChangeKind.ClearHistory, appId);
}
