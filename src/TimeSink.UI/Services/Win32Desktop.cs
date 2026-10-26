using TimeSink.Core.Apps;

namespace TimeSink.UI.Services;

/// <summary>The real adapter for <see cref="IDesktop"/>: window capture, icon extraction and the installed-apps scan.</summary>
internal sealed class Win32Desktop : IDesktop
{
    public static readonly Win32Desktop Instance = new();

    public WindowCapture CaptureWindows()
    {
        var snap = RunningWindows.Capture();
        return new WindowCapture(snap.Windows, snap.AumidByPath);
    }

    public byte[]? ExtractIcon(string exePath, string? aumid, string? imageFile, int size) =>
        IconService.ExtractPng(exePath, aumid, imageFile, size);

    public IReadOnlyList<InstalledApp> ScanInstalled() => InstalledAppsService.Load();
}
