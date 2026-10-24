using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using TimeSink.Core.Data;
using TimeSink.Core.Flyout;
using TimeSink.Core.Ipc;
using TimeSink.Core.Theming;
using TimeSink.UI.Services;
using TimeSink.UI.Theming;
using TimeSink.UI.Views;

namespace TimeSink.UI;

public partial class FlyoutWindow : Window
{
    private const int DipWidth = 420, DipHeight = 620;
    private FlyoutLaunchArgs _launch;
    private IntPtr _hwnd;
    private bool _placed;
    private bool _revealed;
    private bool _rendered;
    private LiveState? _live;

    private readonly ThemeService _themes;
    private readonly string _dbPath;
    private readonly Dictionary<ShellTab, UIElement> _bodies = new();

    /// <param name="standby">Build and render off-screen, then wait for <see cref="Reveal"/>.</param>
    public FlyoutWindow(FlyoutLaunchArgs launch, ThemeService themes, string dbPath, ShellTab initialTab, bool standby = false)
    {
        StartupTrace.Mark("ctor");
        _launch = launch;
        _themes = themes;
        _dbPath = dbPath;
        _revealed = !standby;
        InitializeComponent();
        StartupTrace.Mark("xamlLoaded");
        if (standby)
        {
            ShowActivated = false;
            Left = Top = OffScreen;
        }
        SourceInitialized += OnSourceInitialized;
        ContentRendered += OnContentRendered;
        Deactivated += (_, _) => { if (_revealed && !KeepOpen && !TestKeepOpen) Close(); };
        SettingsButton.Click += (_, _) => ToggleSettings();

        foreach (RadioButton b in TabBar.Children)
        {
            var tab = Enum.Parse<ShellTab>((string)b.Tag);
            b.IsChecked = tab == initialTab;
            b.Checked += (_, _) => Select(tab, persist: true);
        }
        Select(initialTab, persist: false);
        Closed += (_, _) => _live?.Dispose();
    }

    /// <summary>False while a warm standby UI is still off-screen.</summary>
    public bool IsRevealed => _revealed;

    /// <summary>Test aid: TS_KEEPOPEN=1 stops the flyout closing when it loses focus (so it can be screenshotted).</summary>
    private static readonly bool TestKeepOpen = Environment.GetEnvironmentVariable("TS_KEEPOPEN") == "1";

    private void Select(ShellTab tab, bool persist)
    {
        if (!_bodies.TryGetValue(tab, out var body))
            _bodies[tab] = body = CreateBody(tab);
        Body.Content = body;
        _current = tab;
        foreach (var other in _bodies.Values) if (other is TodayView t && !ReferenceEquals(t, body)) t.Stop();
        foreach (var other in _bodies.Values) if (other is YearView y && !ReferenceEquals(y, body)) y.Stop();
        if (_rendered && body is YearView year) year.Start();
        if (_rendered && body is TodayView today) today.Start(); // before first render, OnContentRendered starts it
        if (persist)
        {
            var path = _dbPath;
            var value = tab.ToString();
            Task.Run(() => SettingsStore.Set(path, ShellTabs.SettingKey, value));
        }
    }

    // Stub bodies; real views arrive with later tickets. Built on first visit only.
    private UIElement CreateBody(ShellTab tab)
    {
        if (tab == ShellTab.Today)
        {
            _live ??= new LiveState(PipeNames.For(Environment.GetEnvironmentVariable("TIMESINK_DB")));
            return new TodayView(_dbPath, _themes, _live);
        }
        if (tab == ShellTab.Year)
        {
            _live ??= new LiveState(PipeNames.For(Environment.GetEnvironmentVariable("TIMESINK_DB")));
            return new YearView(_dbPath, _themes, _live);
        }
        var text = new TextBlock { Style = (Style)Application.Current.FindResource("Caption"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Tag = tab };
        text.SetResourceReference(TextBlock.TextProperty, "Str." + char.ToUpperInvariant(StringKeys.StubComingSoon[0]) + StringKeys.StubComingSoon[1..]);
        return text;
    }

    /// <summary>Set while a native dialog (file picker) is open so losing activation does not close the flyout.</summary>
    public bool KeepOpen { get; set; }

    private SettingsView? _settings;
    private ShellTab _current;

    // The Settings page replaces the tab body (and hides the tab bar); the header button becomes "Back".
    private void ToggleSettings()
    {
        if (_settings is not null && ReferenceEquals(Body.Content, _settings))
        {
            TabStrip.Visibility = Visibility.Visible;
            Body.Content = _bodies[_current];
            SettingsButton.SetResourceReference(ContentControl.ContentProperty, "Str.Settings");
            return;
        }
        _settings ??= new SettingsView(_themes, _dbPath);
        TabStrip.Visibility = Visibility.Collapsed;
        Body.Content = _settings;
        SettingsButton.SetResourceReference(ContentControl.ContentProperty, "Str.Back");
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        StartupTrace.Mark("sourceInit");
        _hwnd = new WindowInteropHelper(this).Handle;

        // Rounded corners (Windows 11); ignored on Windows 10.
        var pref = 2; // DWMWCP_ROUND
        DwmSetWindowAttribute(_hwnd, 33, ref pref, sizeof(int));

        if (_revealed) Place();
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);
    }

    private const double OffScreen = -32000;

    /// <summary>Standby only: move the already-rendered window next to the tray icon and take focus.</summary>
    public void Reveal(FlyoutLaunchArgs launch)
    {
        if (_revealed) return;
        var signalled = DateTime.Now;
        _launch = launch;
        _revealed = true;
        Place();
        Activate();
        (Body.Content as TodayView)?.Refresh();
        (Body.Content as YearView)?.Refresh(); // data went stale while waiting off-screen
        // Report once the move has been processed and the frame is idle.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, () =>
            StartupTrace.FlushRevealed((DateTime.Now - signalled).TotalMilliseconds));
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
        _rendered = true;
        // Data loading starts only after the first frame is on screen, so it never delays first paint.
        if (Body.Content is TodayView today) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, today.Start);
        if (Body.Content is YearView yearBody) Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ContextIdle, yearBody.Start);
        if (!_revealed)
        {
            StartupTrace.FlushStandbyReady();
            return;
        }
        Activate();
        StartupTrace.FlushShown();
    }


    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
