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

    public const string Search = "addSearch";
    public const string NoMatch = "addNoMatch";
    public const string InstalledLoading = "addInstalledLoading";
    public const string InstalledEmpty = "addInstalledEmpty";
    public const string StopTracking = "addStopTracking";
    public const string StopTip = "addStopTip";
    public const string Restore = "addRestore";
    public const string RetiredHeader = "addRetiredHeader";
    public const string NotThisOne = "addNotThisOne";
    public const string DropTitle = "addDropTitle";
    public const string DropSub = "addDropSub";
    public const string Choose = "addChoose";
    public const string Places = "addPlaces";
    public const string NotAnApp = "addNotAnApp";
    public const string TargetMissing = "addTargetMissing";
    public const string NoFolder = "addNoFolder";

    public const string TabTracked = "addTabTracked";
    public const string TrackedSearch = "addTrackedSearch";
    public const string TrackedHeader = "addTrackedHeader";
    public const string TrackedEmpty = "addTrackedEmpty";
    public const string TrackedSub = "addTrackedSub";
    public const string TrackedCount = "addTrackedCount";
    public const string Manage = "addManage";
    public const string Rename = "addRename";
    public const string Merge = "addMerge";
    public const string Stop = "addStop";
    public const string RenameLabel = "addRenameLabel";
    public const string Save = "addSave";
    public const string Cancel = "addCancel";
    public const string MergeTitle = "addMergeTitle";
    public const string MergeSub = "addMergeSub";
    public const string MergePreview = "addMergePreview";
    public const string MergePick = "addMergePick";
    public const string MergeInto = "addMergeInto";
    public const string Unmerge = "addUnmerge";
    public const string StopTitle = "addStopTitle";
    public const string StopBody = "addStopBody";
    public const string KeepTracking = "addKeepTracking";
    public const string FilterUntracked = "addFilterUntracked";
    public const string FilterAll = "addFilterAll";
    public const string SubStopped = "addSubStopped";
    public const string SubInstalled = "addSubInstalled";
    public const string MergeStarts = "addMergeStarts";
    public const string MergeCount = "addMergeCount";
    public const string SaveChanges = "addSaveChanges";
    public const string Background = "addBackground";
    public const string BackgroundTip = "addBackgroundTip";
    public const string Kind = "addKind";
    public const string KindTip = "addKindTip";
    public const string KindOther = "addKindOther";
    public const string KindGame = "addKindGame";
    public const string KindGacha = "addKindGacha";

    public static readonly IReadOnlyList<string> All =
    [
        Title, TabRunning, TabInstalled, TabBrowse, RunningHeader, Track, Tracking, OpenFor,
        HintBefore, HintLink, HintAfter, AddedCount, Done, RunningEmpty, Close,
        Search, NoMatch, InstalledLoading, InstalledEmpty, StopTracking, StopTip, Restore, RetiredHeader,
        NotThisOne, DropTitle, DropSub, Choose, Places, NotAnApp, TargetMissing, NoFolder,
        TabTracked, TrackedSearch, TrackedHeader, TrackedEmpty, TrackedSub, TrackedCount, Manage, Rename, Merge, Stop,
        RenameLabel, Save, Cancel, MergeTitle, MergeSub, MergePreview, MergePick, MergeInto, Unmerge, StopTitle, StopBody, KeepTracking,
        FilterUntracked, FilterAll, SubStopped, SubInstalled, MergeStarts, MergeCount, SaveChanges,
        Background, BackgroundTip, Kind, KindTip, KindOther, KindGame, KindGacha,
    ];
}
