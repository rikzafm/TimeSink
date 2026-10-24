using TimeSink.Core.Settings;
using TimeSink.Core.Theming;

namespace TimeSink.Tests.Settings;

public class ResourceUseAndAccentTests
{
    [Fact]
    public void Cpu_percent_is_cpu_time_over_wall_time_per_core()
    {
        // 0.4 s of CPU in 4 s of wall time on 4 cores = 2.5 %.
        ResourceUse.CpuPercent(TimeSpan.FromSeconds(0.4), TimeSpan.FromSeconds(4), cores: 4).Should().BeApproximately(2.5, 0.01);
        ResourceUse.CpuPercent(TimeSpan.Zero, TimeSpan.Zero, 4).Should().Be(0);
    }

    [Theory]
    [InlineData(11_534_336L, 0.0, "11 MB · 0% CPU")]
    [InlineData(48_000_000L, 3.4, "46 MB · 3% CPU")]
    public void Resource_use_is_shown_as_mb_and_cpu(long bytes, double cpu, string expected)
        => ResourceUse.Format(bytes, cpu).Should().Be(expected);

    [Fact]
    public void Resource_use_without_an_agent_says_so()
        => ResourceUse.Format(null, 0).Should().Be("Agent not running");

    private static Theme Load(string id)
        => new ThemeLoader(new FolderThemeSource(Path.Combine(AppContext.BaseDirectory, "themes"))).Load(id);

    [Theory]
    [InlineData("pulse-dark", "Lime", "#c6f26b")]
    [InlineData("pulse-dark", "Blue", "#6bb6ff")]
    [InlineData("pulse-dark", "Pink", "#ff8fc7")]
    [InlineData("pulse-light", "Lime", "#4f7a0a")]
    [InlineData("pulse-light", "Blue", "#1f6fc4")]
    [InlineData("pulse-light", "Pink", "#c2185b")]
    public void Accent_overrides_accent_and_the_top_of_the_heat_ramp(string themeId, string accent, string hex)
    {
        var original = Load(themeId);
        var t = Accents.Apply(original, accent);
        t.Colors["accent"].Should().Be(hex);
        t.Heat[^1].Should().Be(hex);
        t.Heat[0].Should().Be(original.Heat[0]);
        t.Colors["surface"].Should().Be(original.Colors["surface"]);
    }

    [Fact]
    public void Unknown_accent_leaves_the_theme_alone()
    {
        var d = Load("pulse-dark");
        Accents.Apply(d, "Teal").Colors["accent"].Should().Be(d.Colors["accent"]);
    }
}
