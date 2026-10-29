namespace TimeSink.Core.Apps;

/// <summary>The open windows at one instant plus the AUMID of each packaged app's exe path.</summary>
public sealed record WindowCapture(IReadOnlyList<OpenWindow> Windows, IReadOnlyDictionary<string, string> AumidByPath);

/// <summary>
/// The seam between <see cref="AppCatalogue"/> and the desktop (Win32, shell). The real adapter lives
/// in the UI project; tests use a fake. Calls block, so call the catalogue from a pool thread.
/// </summary>
public interface IDesktop
{
    /// <summary>Enumerate the top-level windows now.</summary>
    WindowCapture CaptureWindows();

    /// <summary>Every installed app (Start Menu shortcuts, packaged apps, Steam games).</summary>
    IReadOnlyList<InstalledApp> ScanInstalled();
}
