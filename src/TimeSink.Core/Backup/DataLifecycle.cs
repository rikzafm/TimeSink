using Microsoft.Data.Sqlite;

namespace TimeSink.Core.Backup;

/// <summary>How the Agent is told its database changed underneath it (the seam <see cref="DataLifecycle"/> signals through).</summary>
public interface IAgentSignal
{
    /// <summary>The Agent drops its in-memory open sessions so they are re-detected against the new content.</summary>
    void Reset();
}

/// <summary>The named event the Agent waits on (<see cref="AgentSignals.ResetEventName"/>); a no-op when the Agent is not running.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class NamedEventAgentSignal(string eventName = AgentSignals.ResetEventName) : IAgentSignal
{
    public void Reset()
    {
        try { using var ev = EventWaitHandle.OpenExisting(eventName); ev.Set(); }
        catch (WaitHandleCannotBeOpenedException) { }
    }
}

/// <summary>What a data operation did: it worked, or it failed in a way the user can read (the message of the failure).</summary>
public sealed record DataResult(string? Error = null)
{
    public bool Succeeded => Error is null;
}

public sealed record DataResult<T>(T? Value, string? Error = null)
{
    public bool Succeeded => Error is null;
}

/// <summary>
/// Every operation that backs up, restores, exports or deletes the user's data, over one database. The expected failures
/// (an unusable backup, a locked database, a file that cannot be written) come back as a <see cref="DataResult"/> with the
/// message, never as an exception. Replace restore and Delete all history rewrite the database underneath the Agent, so
/// each tells it through <see cref="IAgentSignal"/> once it has succeeded: no caller has to remember to.
/// </summary>
public sealed class DataLifecycle(string dbPath, IAgentSignal agent)
{
    /// <summary>"Back up now": the file written.</summary>
    public DataResult<string> BackUpNow(DateTimeOffset now, string defaultFolder, string appVersion) =>
        Try(() => AutoBackup.Now(dbPath, now, defaultFolder, appVersion));

    public DataResult<BackupInspection> Inspect(string backupFile) => Try(() => RestoreService.Inspect(backupFile));

    /// <summary>Adds what the backup has and the database lacks. The Agent is not told: nothing it holds changes.</summary>
    public DataResult<MergeResult> Merge(string backupFile, TimeZoneInfo zone) =>
        Try(() => RestoreService.Merge(backupFile, dbPath, zone));

    /// <summary>Swaps the database content for the backup's, after a safety copy in the backup folder.</summary>
    public DataResult<ReplaceResult> ReplaceFromBackup(string backupFile, string defaultFolder, DateTimeOffset now, TimeZoneInfo zone)
    {
        var result = Try(() => RestoreService.Replace(backupFile, dbPath, AutoBackup.Folder(dbPath, defaultFolder), now, zone));
        if (result.Succeeded) agent.Reset();
        return result;
    }

    public DataResult<CsvExportFiles> ExportCsv(string folder) => Try(() => DataExport.ExportCsv(dbPath, folder));

    public DataResult ExportJson(string file) => Try(() => { DataExport.ExportJson(dbPath, file); return true; }).ToResult();

    /// <summary>Removes every session, daily total and icon; the app list and settings stay. Needs the exact confirmation word.</summary>
    public DataResult DeleteAllHistory(string typedConfirmation)
    {
        if (!string.Equals(typedConfirmation, HistoryDeleter.Confirmation, StringComparison.Ordinal))
            return new DataResult($"Type {HistoryDeleter.Confirmation} to confirm.");
        var result = Try(() => { HistoryDeleter.DeleteAll(dbPath, typedConfirmation); return true; }).ToResult();
        if (result.Succeeded) agent.Reset();
        return result;
    }

    private static DataResult<T> Try<T>(Func<T> operation)
    {
        try { return new DataResult<T>(operation()); }
        catch (Exception ex) when (ex is BackupException or SqliteException or IOException or UnauthorizedAccessException)
        {
            return new DataResult<T>(default, ex.Message);
        }
    }
}

internal static class DataResultExtensions
{
    public static DataResult ToResult<T>(this DataResult<T> result) => new(result.Error);
}
