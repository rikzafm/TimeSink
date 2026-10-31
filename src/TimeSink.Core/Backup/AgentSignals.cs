namespace TimeSink.Core.Backup;

/// <summary>Named kernel objects shared by the UI and the Agent (until the IPC channel of ticket 06 carries them).</summary>
public static class AgentSignals
{
    /// <summary>
    /// Set by the UI after Replace restore or Delete all history rewrote the database: the Agent drops its in-memory
    /// open sessions so they are re-detected against the new content.
    /// </summary>
    public const string ResetEventName = @"Local\TimeSink.Agent.Reset";

    /// <summary>
    /// Set by the UI when the user clicks "Sync now": the Agent immediately exports and scans for imports.
    /// </summary>
    public const string SyncNowEventName = @"Local\TimeSink.Agent.SyncNow";
}
