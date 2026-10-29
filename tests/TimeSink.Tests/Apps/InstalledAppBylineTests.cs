using TimeSink.Core.Apps;

namespace TimeSink.Tests.Apps;

public sealed class InstalledAppBylineTests
{
    private static InstalledApp App(string? publisher, string? source = null) => new("App", @"c:\a\app.exe", null, null, publisher, source);

    [Fact]
    public void A_Steam_game_shows_Steam_even_with_a_publisher()
    {
        App("Valve", InstalledApp.SteamSource).Byline.Should().Be("Steam");
    }

    [Fact]
    public void Other_apps_show_their_publisher()
    {
        App("Acme").Byline.Should().Be("Acme");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void An_app_with_neither_has_no_byline(string? publisher)
    {
        App(publisher).Byline.Should().BeNull();
    }
}
