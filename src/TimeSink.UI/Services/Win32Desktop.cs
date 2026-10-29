using TimeSink.Core.Apps;

namespace TimeSink.UI.Services;

/// <summary>The real adapter for <see cref="IDesktop"/>: window capture and the installed-apps scan.</summary>
internal sealed class Win32Desktop : IDesktop
{
    public static readonly Win32Desktop Instance = new();

    public WindowCapture CaptureWindows()
    {
        var snap = RunningWindows.Capture();
        return new WindowCapture(snap.Windows, snap.AumidByPath);
    }

    public IReadOnlyList<InstalledApp> ScanInstalled()
    {
        try { return InstalledAppsService.Load(); }
        catch { return []; } // as the Installed tab does: a failed scan lists nothing rather than breaking the screen
    }
}
