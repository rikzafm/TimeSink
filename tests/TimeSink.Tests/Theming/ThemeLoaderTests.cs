using TimeSink.Core.Theming;

namespace TimeSink.Tests.Theming;

public class ThemeLoaderTests
{
    private static readonly string ThemesDir = Path.Combine(AppContext.BaseDirectory, "themes");

    private static ThemeLoader RealLoader() => new(new FolderThemeSource(ThemesDir));

    private static string DarkJson() => File.ReadAllText(Path.Combine(ThemesDir, "pulse-dark", "theme.json"));

    [Fact]
    public void Pulse_dark_loads_the_specified_tokens()
    {
        var t = RealLoader().Load("pulse-dark");

        t.Id.Should().Be("pulse-dark");
        t.Colors["surface"].Should().Be("#11140f");
        t.Colors["accent"].Should().Be("#c6f26b");
        t.Heat.Should().HaveCount(5);
        t.Fonts["ui"].Should().Be("Segoe UI Variable Text");
        t.Radius.Should().Equal(8, 10, 12);
        t.Layout.Should().Be("pulse");
    }

    [Fact]
    public void Pulse_light_overrides_colours_and_inherits_the_rest()
    {
        var t = RealLoader().Load("pulse-light");

        t.Id.Should().Be("pulse-light");
        t.Colors["surface"].Should().Be("#f6f7f2");
        t.Fonts["ui"].Should().Be("Segoe UI Variable Text");
        t.Strings.Get("tabToday").Should().Be("Today");
    }

    [Theory]
    [InlineData("pulse-dark")]
    [InlineData("pulse-light")]
    public void Builtins_define_every_catalogued_token_and_string(string id)
    {
        var t = RealLoader().Load(id);

        foreach (var key in TokenCatalog.ColorKeys) t.Colors.Should().ContainKey(key);
        foreach (var key in StringKeys.All) t.Strings.Has(key).Should().BeTrue($"string '{key}'");
    }

    [Fact]
    public void Extends_fills_missing_tokens_and_strings_from_the_parent()
    {
        var source = new MemoryThemeSource
        {
            ["pulse-dark"] = DarkJson(),
            ["mine"] = """{"schema":1,"id":"mine","extends":"pulse-dark","colors":{"accent":"#112233"},"strings":{"addApp":"Add"}}""",
        };

        var t = new ThemeLoader(source).Load("mine");

        t.Colors["accent"].Should().Be("#112233");
        t.Colors["surface"].Should().Be("#11140f");
        t.Strings.Get("addApp").Should().Be("Add");
        t.Strings.Get("tabWeek").Should().Be("Week");
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("""{"schema":1,"id":"bad","extends":"pulse-dark","colors":{"accent":"purple"}}""")]
    [InlineData("""{"schema":1,"id":"bad","extends":"pulse-dark","layout":"arena"}""")]
    [InlineData("""{"schema":1,"id":"bad","extends":"nope"}""")]
    [InlineData("""{"schema":1,"id":"bad","extends":"bad"}""")]
    [InlineData("""{"schema":1,"id":"bad","colors":{"surface":"#000000"}}""")]
    public void A_corrupt_theme_reverts_to_pulse_dark(string badJson)
    {
        var source = new MemoryThemeSource
        {
            ["pulse-dark"] = DarkJson(),
            ["bad"] = badJson,
        };

        var t = new ThemeLoader(source).Load("bad");

        t.Id.Should().Be("pulse-dark");
        t.Colors["accent"].Should().Be("#c6f26b");
    }

    [Fact]
    public void A_missing_theme_reverts_to_pulse_dark()
    {
        RealLoader().Load("does-not-exist").Id.Should().Be("pulse-dark");
    }

    [Fact]
    public void Unknown_string_key_returns_a_visible_placeholder_not_an_exception()
    {
        RealLoader().Load("pulse-dark").Strings.Get("nope").Should().Be("[nope]");
    }
}

internal sealed class MemoryThemeSource : Dictionary<string, string>, IThemeSource
{
    public string? Read(string id) => TryGetValue(id, out var v) ? v : null;
}
