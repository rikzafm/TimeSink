using System.IO;
using System.Runtime.InteropServices;
using TimeSink.Core.Data;
using TimeSink.Core.Settings;
using TimeSink.Core.Theming;
using TimeSink.UI.Theming;

namespace TimeSink.UI;

/// <summary>What the first window needs, computed off the UI thread while WPF itself starts up.</summary>
internal sealed record Prepared(Theme Theme, ShellTab Tab, string DbPath, bool Compact);

internal static class Program
{
    public static Task<Prepared> Prepare { get; private set; } = null!;

    [STAThread]
    public static int Main()
    {
        StartupTrace.Mark("main");
        // Theme JSON + settings reads overlap with WPF/Application construction (the dominant cold-start cost).
        Prepare = Task.Run(PrepareStartup);
        // Software rendering: this flat 420x620 UI does not need the GPU, and skipping D3D device setup saves
        // about 250 ms on a cold start (measured). TS_HW=1 re-enables hardware rendering for comparison.
        if (Environment.GetEnvironmentVariable("TS_HW") != "1")
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    private static Prepared PrepareStartup()
    {
        var dbPath = Environment.GetEnvironmentVariable("TIMESINK_DB") is { Length: > 0 } over
            ? over
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeSink", "timesink.db");
        var shift = (GetAsyncKeyState(0x10) & 0x8000) != 0; // Shift forces pulse-dark for this session
        var settings = AppSettings.ForFile(dbPath);
        var id = shift ? ThemeLoader.DefaultId : ThemeService.IdFor(settings.Get(Setting.Theme), ThemeService.SystemIsLight());
        var theme = Accents.Apply(ThemeService.Load(id), settings.Get(Setting.Accent));
        var tab = settings.Get(Setting.LastTab);
        StartupTrace.Mark("prepared");
        return new Prepared(theme, tab, dbPath, settings.Get(Setting.Compact));
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
