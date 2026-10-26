using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using TimeSink.Core.Flyout;
using TimeSink.Core.Usage;
using TimeSink.UI.Theming;

namespace TimeSink.UI;

public partial class App : Application
{
    public const string MutexName = @"Local\TimeSink.UI";
    public const string CloseEventName = @"Local\TimeSink.UI.Close";
    public const string ShowEventName = @"Local\TimeSink.UI.Show";
    public const string StandbyArg = "--standby";
    public const string KeepArg = "--keep";
    private static readonly TimeSpan StandbyTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Where the Agent leaves the launch geometry for a standby UI (see <see cref="FlyoutLaunchArgs.ToFileText"/>).</summary>
    public static string HandoffPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeSink", "flyout.args");

    private Mutex? _mutex;
    private EventWaitHandle? _closeEvent, _showEvent;
    private RegisteredWaitHandle? _closeWait, _showWait;

    protected override void OnStartup(StartupEventArgs e)
    {
        StartupTrace.Mark("onStartup");
        base.OnStartup(e);
        // The refresh methods are async void and catch only database errors; anything else would end the process
        // from a background refresh, so a failed refresh is dropped and the next tick tries again.
        DispatcherUnhandledException += (_, args) =>
        {
            System.Diagnostics.Debug.WriteLine(args.Exception);
            args.Handled = true;
        };
        TimeSink.UI.Services.SmoothScroll.Install();

        // Story 48: formatting follows the user's Windows regional settings (never overridden, including the 12/24 h
        // clock). Threads that WPF or tasks create start from the same culture, and XAML text formatting (default en-US)
        // is told to use it too.
        var culture = ClockFormat.WindowsCulture();
        CultureInfo.CurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(System.Windows.Markup.XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        _mutex = new Mutex(true, MutexName, out var isFirst);
        if (!isFirst)
        {
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        var standby = e.Args.Contains(StandbyArg);
        var keep = e.Args.Contains(KeepArg);

        if (!FlyoutLaunchArgs.TryParse(e.Args, out var launch))
        {
            // Started by hand (or standby, before the Agent says where): bottom-right of the primary work area.
            var wa = SystemParameters.WorkArea;
            var screen = new PxRect(0, 0, (int)SystemParameters.PrimaryScreenWidth, (int)SystemParameters.PrimaryScreenHeight);
            var work = new PxRect((int)wa.Left, (int)wa.Top, (int)wa.Right, (int)wa.Bottom);
            launch = new FlyoutLaunchArgs(new PxRect(work.Right - 24, work.Bottom, work.Right, work.Bottom + 24), screen, work, 96);
        }

        var prepared = Program.Prepare.GetAwaiter().GetResult();
        var themes = new ThemeService { Compact = prepared.Compact };
        themes.Apply(prepared.Theme);
        StartupTrace.Mark("themed");

        var window = new FlyoutWindow(launch, themes, prepared.DbPath, prepared.Tab, standby);
        StartupTrace.Mark("windowBuilt");

        _closeEvent = new EventWaitHandle(false, EventResetMode.AutoReset, CloseEventName);
        _closeWait = ThreadPool.RegisterWaitForSingleObject(_closeEvent,
            (_, _) => Dispatcher.BeginInvoke(window.Close), null, Timeout.Infinite, false);

        if (standby)
        {
            // Wait (off-screen, already rendered) for the Agent's Show signal; give up unless asked to stay.
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, timedOut) =>
            {
                if (timedOut) { Dispatcher.BeginInvoke(window.Close); return; }
                Dispatcher.BeginInvoke(() =>
                {
                    string text;
                    try { text = File.ReadAllText(HandoffPath); } catch (IOException) { window.Close(); return; }
                    if (FlyoutLaunchArgs.TryParseFileText(text, out var l)) window.Reveal(l);
                    else window.Close();
                });
            }, null, keep ? Timeout.InfiniteTimeSpan : StandbyTimeout, true);
        }

        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _closeWait?.Unregister(null);
        _showWait?.Unregister(null);
        _closeEvent?.Dispose();
        _showEvent?.Dispose();
        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
        base.OnExit(e);
    }
}
