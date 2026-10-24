using TimeSink.Core.Apps;

namespace TimeSink.Tests.Apps;

public sealed class RunningAppsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);

    private static OpenWindow Win(string path, string name = "App", int minutesAgo = 10, bool visible = true,
        bool cloaked = false, bool tool = false, string? family = null) =>
        new(path, name, T0.AddMinutes(-minutesAgo), visible, cloaked, tool, family);

    private static IReadOnlyList<RunningApp> Select(IEnumerable<OpenWindow> w, IEnumerable<string>? paths = null, IEnumerable<string>? families = null) =>
        RunningApps.Select(w, new ExcludedApps(paths ?? [], families ?? []));

    [Fact]
    public void Only_visible_non_cloaked_non_tool_windows_are_listed()
    {
        var list = Select([
            Win(@"c:\a\shown.exe", "Shown"),
            Win(@"c:\a\hidden.exe", visible: false),
            Win(@"c:\a\suspended.exe", cloaked: true),
            Win(@"c:\a\tray.exe", tool: true),
        ]);
        list.Select(a => a.DisplayName).Should().Equal("Shown");
    }

    [Fact]
    public void Tracked_and_ignored_paths_and_tracked_package_families_are_excluded_case_insensitively()
    {
        var list = Select([
            Win(@"C:\A\Tracked.exe", "Tracked"),
            Win(@"c:\a\ignored.exe", "Ignored"),
            Win(@"c:\store\v2\calc.exe", "Calc", family: "Microsoft.Calc_8wekyb"),
            Win(@"c:\a\free.exe", "Free"),
        ], paths: [@"c:\a\tracked.EXE", @"C:\A\IGNORED.exe"], families: ["microsoft.calc_8wekyb"]);
        list.Select(a => a.DisplayName).Should().Equal("Free");
    }

    [Fact]
    public void An_app_with_several_windows_appears_once_with_its_earliest_start_and_longest_open_comes_first()
    {
        var list = Select([
            Win(@"c:\a\new.exe", "New", minutesAgo: 5),
            Win(@"c:\a\old.exe", "Old", minutesAgo: 200),
            Win(@"c:\a\new.exe", "New", minutesAgo: 30),
        ]);
        list.Select(a => (a.DisplayName, a.StartUtc)).Should().Equal(
            ("Old", T0.AddMinutes(-200)), ("New", T0.AddMinutes(-30)));
    }

    [Theory]
    [InlineData("pulse-dark")]
    [InlineData("pulse-light")]
    public void Builtin_themes_define_every_Add_app_string(string id)
    {
        var loader = new TimeSink.Core.Theming.ThemeLoader(new TimeSink.Core.Theming.FolderThemeSource(Path.Combine(AppContext.BaseDirectory, "themes")));
        var t = loader.Load(id);
        foreach (var key in AddAppStrings.All) t.Strings.Has(key).Should().BeTrue($"string '{key}'");
    }

    [Fact]
    public void Windows_whose_process_path_is_unknown_are_skipped()
    {
        Select([new OpenWindow(null, "Elevated", T0, true, false, false, null)]).Should().BeEmpty();
    }
}
