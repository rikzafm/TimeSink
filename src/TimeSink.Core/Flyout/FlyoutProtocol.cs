namespace TimeSink.Core.Flyout;

/// <summary>
/// What the Agent and the UI process agree on when the Agent starts, wakes and closes the flyout: the named events, the
/// command-line flags and the file the launch geometry goes through. Both processes read these, so they cannot disagree.
/// </summary>
public static class FlyoutProtocol
{
    /// <summary>Set by the Agent to close a UI that is showing.</summary>
    public const string CloseEventName = @"Local\TimeSink.UI.Close";

    /// <summary>Set by the Agent to reveal a standby UI, after it has written <see cref="HandoffPath"/>.</summary>
    public const string ShowEventName = @"Local\TimeSink.UI.Show";

    /// <summary>The UI builds and renders off-screen, then waits for <see cref="ShowEventName"/> (or exits when idle).</summary>
    public const string StandbyArg = "--standby";

    /// <summary>With <see cref="StandbyArg"/>: wait for the Show signal for as long as the UI lives.</summary>
    public const string KeepArg = "--keep";

    public const string HandoffFileName = "flyout.args";

    /// <summary>Where the Agent leaves the launch geometry for a standby UI (see <see cref="FlyoutLaunchArgs.ToFileText"/>).</summary>
    public static string HandoffPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeSink", HandoffFileName);

    /// <summary>The command line that starts a standby UI.</summary>
    public static string[] StandbyArgs(bool keep) => keep ? [StandbyArg, KeepArg] : [StandbyArg];
}
