using TimeSink.Core.Theming;

namespace TimeSink.Tests.Theming;

public class AppDetailStringTests
{
    [Theory]
    [InlineData("pulse-dark")]
    [InlineData("pulse-light")]
    public void Themes_define_every_app_detail_string(string id)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "themes");
        var t = new ThemeLoader(new FolderThemeSource(dir)).Load(id);
        foreach (var key in AppDetailStringKeys.All) t.Strings.Has(key).Should().BeTrue($"string '{key}'");
    }
}
