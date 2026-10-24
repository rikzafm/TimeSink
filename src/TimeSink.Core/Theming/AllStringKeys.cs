namespace TimeSink.Core.Theming;

/// <summary>
/// String keys of the All tab (kept apart from <see cref="StringKeys"/> so parallel tickets do not collide). Built-in
/// themes must define all of them; values with <c>{n}</c> are composed by the view.
/// </summary>
public static class AllStringKeys
{
    public const string Caption = "allCaption";
    public const string TotalHours = "allTotalHours";
    public const string TrackedSuffix = "allTrackedSuffix";
    public const string RetiredSuffix = "allRetiredSuffix";
    public const string SortedBy = "allSortedBy";
    public const string SortBy = "allSortBy";
    public const string ColApp = "allColApp";
    public const string ColHours = "allColHours";
    public const string ColDays = "allColDays";
    public const string ColAvg = "allColAvg";
    public const string ColShare = "allColShare";
    public const string RetiredHeader = "allRetiredHeader";
    public const string Stopped = "allStopped";
    public const string TotalRow = "allTotalRow";
    public const string Note = "allNote";
    public const string NoteNone = "allNoteNone";
    public const string Hours = "allHours";
    public const string Empty = "allEmpty";
    public const string ArrowDesc = "allArrowDesc";
    public const string ArrowAsc = "allArrowAsc";
    public const string Export = "allExport";
    public const string ExportDone = "allExportDone";

    public static readonly IReadOnlyList<string> All =
    [
        Caption, TotalHours, TrackedSuffix, RetiredSuffix, SortedBy, SortBy, ColApp, ColHours, ColDays, ColAvg, ColShare,
        RetiredHeader, Stopped, TotalRow, Note, NoteNone, Hours, Empty, ArrowDesc, ArrowAsc, Export, ExportDone,
    ];
}
