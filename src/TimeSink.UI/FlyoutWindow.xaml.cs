using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using TimeSink.Core.Flyout;

namespace TimeSink.UI;

public partial class FlyoutWindow : Window
{
    private const int DipWidth = 420, DipHeight = 620;
    private readonly FlyoutLaunchArgs _launch;
    private IntPtr _hwnd;
    private bool _placed;

    public FlyoutWindow(FlyoutLaunchArgs launch)
    {
        _launch = launch;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        ContentRendered += OnContentRendered;
        Deactivated += (_, _) => Close();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        // Rounded corners (Windows 11); ignored on Windows 10.
        var pref = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(_hwnd, 33, ref pref, sizeof(int));

        Place();
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
    }

    // The anchor monitor's DPI may differ from the DPI WPF started with; WM_DPICHANGED makes WPF
    // rescale, so re-apply the physical-pixel placement afterwards.
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_DPICHANGED = 0x02E0;
        if (msg == WM_DPICHANGED && _placed) Dispatcher.BeginInvoke(Place);
        return IntPtr.Zero;
    }

    private void Place()
    {
        var size = new PxSize((int)Math.Round(DipWidth * _launch.Dpi / 96.0), (int)Math.Round(DipHeight * _launch.Dpi / 96.0));
        var r = FlyoutPlacement.Compute(_launch.Anchor, _launch.Monitor, _launch.Work, size);
        const uint SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
        SetWindowPos(_hwnd, IntPtr.Zero, r.Left, r.Top, r.Width, r.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        _placed = true;
    }

    private void OnContentRendered(object? sender, EventArgs e)
    {
        Activate();
        Log($"FlyoutShown ms={SinceStart():0}");
    }

    // Process creation time is read once per call from the OS; cheap enough for a one-off measurement.
    private static double SinceStart()
    {
        using var self = Process.GetCurrentProcess();
        return (DateTime.Now - self.StartTime).TotalMilliseconds;
    }

    private static void Log(string line)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeSink", "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "ui.log"), $"{DateTime.Now:O} {line}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
