using System.Threading;
using System.Windows;
using TimeSink.Core.Flyout;

namespace TimeSink.UI;

public partial class App : Application
{
    public const string MutexName = @"Local\TimeSink.UI";
    public const string CloseEventName = @"Local\TimeSink.UI.Close";

    private Mutex? _mutex;
    private EventWaitHandle? _closeEvent;
    private RegisteredWaitHandle? _closeWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        if (!FlyoutLaunchArgs.TryParse(e.Args, out var launch))
        {
            // Started by hand: fall back to the bottom-right of the primary work area.
            var wa = SystemParameters.WorkArea;
            var screen = new PxRect(0, 0, (int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight);
            var work = new PxRect((int)wa.Left, (int)wa.Top, (int)wa.Right, (int)wa.Bottom);
            launch = new FlyoutLaunchArgs(new PxRect(work.Right - 24, work.Bottom, work.Right, work.Bottom + 24), screen, work, 96);
        }

        var window = new FlyoutWindow(launch);

        _closeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, CloseEventName);
        _closeWait = ThreadPool.RegisterWaitForSingleObject(_closeEvent,
            (_, _) => Dispatcher.BeginInvoke(window.Close), null, Timeout.Infinite, false);

        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _closeWait?.Unregister(null);
        _closeEvent?.Dispose();
        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
        base.OnExit(e);
    }
}
