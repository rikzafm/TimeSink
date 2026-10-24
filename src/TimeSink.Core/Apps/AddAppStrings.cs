namespace TimeSink.Core.Apps;

/// <summary>String-table keys of the Add app screen (kept apart from <c>StringKeys</c> so parallel tickets do not collide).</summary>
public static class AddAppStrings
{
    public const string Title = "addAppTitle";
    public const string TabRunning = "addTabRunning";
    public const string TabInstalled = "addTabInstalled";
    public const string TabBrowse = "addTabBrowse";
    public const string RunningHeader = "addRunningHeader";
    public const string Track = "addTrack";
    public const string Tracking = "addTracking";
    public const string OpenFor = "addOpenFor";
    public const string HintBefore = "addHintBefore";
    public const string HintLink = "addHintLink";
    public const string HintAfter = "addHintAfter";
    public const string AddedCount = "addAddedCount";
    public const string Done = "addDone";
    public const string RunningEmpty = "addRunningEmpty";
    public const string Close = "addClose";

    public static readonly IReadOnlyList<string> All =
    [
        Title, TabRunning, TabInstalled, TabBrowse, RunningHeader, Track, Tracking, OpenFor,
        HintBefore, HintLink, HintAfter, AddedCount, Done, RunningEmpty, Close,
    ];
}
