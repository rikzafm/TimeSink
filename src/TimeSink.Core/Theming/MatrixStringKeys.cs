namespace TimeSink.Core.Theming;

/// <summary>
/// String keys of the Year > Matrix sub-view (kept apart from <see cref="StringKeys"/>, like <see cref="YearStringKeys"/>,
/// so parallel tickets do not collide). Built-in themes must define all of them; values with <c>{n}</c> are composed by the view.
/// </summary>
public static class MatrixStringKeys
{
    public const string Caption = "matrixCaption";
    public const string CaptionSince = "matrixCaptionSince";
    public const string Total = "matrixTotal";
    public const string AllApps = "matrixAllApps";
    public const string Footnote = "matrixFootnote";
    public const string Hint = "matrixHint";
    public const string Hours = "matrixHours";
    public const string RowTotal = "matrixRowTotal";
    public const string RowNotePeak = "matrixRowNotePeak";
    public const string RowNoteUp = "matrixRowNoteUp";
    public const string RowNoteDown = "matrixRowNoteDown";
    public const string MonthNote1 = "matrixMonthNote1";
    public const string MonthNote2 = "matrixMonthNote2";
    public const string MonthNote3 = "matrixMonthNote3";
    public const string MonthNoteNone = "matrixMonthNoteNone";
    public const string Empty = "matrixEmpty";

    public static readonly IReadOnlyList<string> All =
    [
        Caption, CaptionSince, Total, AllApps, Footnote, Hint, Hours, RowTotal, RowNotePeak, RowNoteUp, RowNoteDown,
        MonthNote1, MonthNote2, MonthNote3, MonthNoteNone, Empty,
    ];
}
