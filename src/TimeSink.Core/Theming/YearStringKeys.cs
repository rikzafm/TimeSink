namespace TimeSink.Core.Theming;

/// <summary>
/// String keys of the Year tab (kept apart from <see cref="StringKeys"/> so parallel tickets do not collide). Built-in
/// themes must define all of them; values with <c>{n}</c> are composed by the view. Tickets 13/14 add their own
/// <c>YearWrapped*</c> / <c>YearMatrix*</c> key classes the same way.
/// </summary>
public static class YearStringKeys
{
    public const string SwitchPace = "yearSwitchPace";
    public const string SwitchWrapped = "yearSwitchWrapped";
    public const string SwitchMatrix = "yearSwitchMatrix";

    public const string Caption = "yearCaption";
    public const string Hours = "yearHours";
    public const string Average = "yearAverage";
    public const string Best = "yearBest";
    public const string BestValue = "yearBestValue";
    public const string Banner = "yearBanner";
    public const string BannerOne = "yearBannerOne";
    public const string BannerNone = "yearBannerNone";
    public const string LegendTracked = "yearLegendTracked";
    public const string LegendProjected = "yearLegendProjected";
    public const string LegendAverage = "yearLegendAverage";
    public const string TapHint = "yearTapHint";
    public const string ProjectedSuffix = "yearProjectedSuffix";
    public const string NoteBefore = "yearNoteBefore";
    public const string NoteBeforeOn = "yearNoteBeforeOn";
    public const string NoteFirst = "yearNoteFirst";
    public const string NoteProjected = "yearNoteProjected";
    public const string NoteCurrent = "yearNoteCurrent";
    public const string NoteCurrentVs = "yearNoteCurrentVs";
    public const string NoteVs = "yearNoteVs";
    public const string NoteBest = "yearNoteBest";
    public const string NoteNoApps = "yearNoteNoApps";
    public const string Empty = "yearEmpty";

    public static readonly IReadOnlyList<string> All =
    [
        SwitchPace, SwitchWrapped, SwitchMatrix, Caption, Hours, Average, Best, BestValue, Banner, BannerOne, BannerNone,
        LegendTracked, LegendProjected, LegendAverage, TapHint, ProjectedSuffix, NoteBefore, NoteBeforeOn, NoteFirst,
        NoteProjected, NoteCurrent, NoteCurrentVs, NoteVs, NoteBest, NoteNoApps, Empty,
    ];
}
