using TimeSink.Core.Theming;

namespace TimeSink.Tests.Theming;

public class WrappedStringTests
{
    [Theory]
    [InlineData("pulse-dark")]
    [InlineData("pulse-light")]
    public void Themes_define_every_wrapped_string(string id)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "themes");
        var t = new ThemeLoader(new FolderThemeSource(dir)).Load(id);
        foreach (var key in WrappedStringKeys.All) t.Strings.Has(key).Should().BeTrue($"string '{key}'");
    }
}
