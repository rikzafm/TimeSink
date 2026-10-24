namespace TimeSink.Core.Settings;

/// <summary>String-table keys of the Settings page's backup, restore, export and delete flows (decision 114). Built-in themes must define all of them.</summary>
public static class SettingsStrings
{
    public const string BackingUp = "setBackingUp";
    public const string BackupDone = "setBackupDone";
    public const string BackupFailedShort = "setBackupFailedShort";
    public const string BackupFailed = "setBackupFailed";
    public const string BackUp = "setBackUp";
    public const string RestoreTitle = "setRestoreTitle";
    public const string RestoreFilter = "setRestoreFilter";
    public const string RestoreCannot = "setRestoreCannot";
    public const string RestoreDetail = "setRestoreDetail";
    public const string RestoreUpgrade = "setRestoreUpgrade";
    public const string RestoreBody = "setRestoreBody";
    public const string Cancel = "setCancel";
    public const string Merge = "setMerge";
    public const string Replace = "setReplace";
    public const string Ok = "setOk";
    public const string Replacing = "setReplacing";
    public const string Merging = "setMerging";
    public const string Restoring = "setRestoring";
    public const string Replaced = "setReplaced";
    public const string SafetyCopy = "setSafetyCopy";
    public const string Merged = "setMerged";
    public const string RestoreFailed = "setRestoreFailed";
    public const string ReplaceFinished = "setReplaceFinished";
    public const string MergeFinished = "setMergeFinished";
    public const string ExportTitle = "setExportTitle";
    public const string ExportBody = "setExportBody";
    public const string Json = "setJson";
    public const string Csv = "setCsv";
    public const string ExportFolderTitle = "setExportFolderTitle";
    public const string ExportSavedTwo = "setExportSavedTwo";
    public const string ExportJsonTitle = "setExportJsonTitle";
    public const string ExportJsonFilter = "setExportJsonFilter";
    public const string ExportSaved = "setExportSaved";
    public const string ExportFailed = "setExportFailed";
    public const string ExportResultTitle = "setExportResultTitle";
    public const string DeleteTitle = "setDeleteTitle";
    public const string DeleteBody = "setDeleteBody";
    public const string DeleteEverything = "setDeleteEverything";
    public const string DeleteDone = "setDeleteDone";
    public const string DeleteFailed = "setDeleteFailed";
    public const string ChooseFile = "setChooseFile";
    public const string ExportButton = "setExportButton";
    public const string DeleteButton = "setDeleteButton";
    public const string IconTitle = "setIconTitle";
    public const string IconSubtitle = "setIconSubtitle";
    public const string IconApply = "setIconApply";
    public const string IconReset = "setIconReset";

    public static readonly IReadOnlyList<string> All =
    [
        BackingUp, BackupDone, BackupFailedShort, BackupFailed, BackUp, RestoreTitle, RestoreFilter, RestoreCannot, RestoreDetail, RestoreUpgrade, RestoreBody, Cancel, Merge, Replace, Ok, Replacing, Merging, Restoring, Replaced, SafetyCopy, Merged, RestoreFailed, ReplaceFinished, MergeFinished, ExportTitle, ExportBody, Json, Csv, ExportFolderTitle, ExportSavedTwo, ExportJsonTitle, ExportJsonFilter, ExportSaved, ExportFailed, ExportResultTitle, DeleteTitle, DeleteBody, DeleteEverything, DeleteDone, DeleteFailed, ChooseFile, ExportButton, DeleteButton, IconTitle, IconSubtitle, IconApply, IconReset,
    ];
}
