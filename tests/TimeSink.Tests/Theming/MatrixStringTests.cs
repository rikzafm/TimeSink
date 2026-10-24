using TimeSink.Core.Theming;

namespace TimeSink.Tests.Theming;

public class MatrixStringTests
{
    [Theory]
    [InlineData("pulse-dark")]
    [InlineData("pulse-light")]
    public void Themes_define_every_matrix_string(string id)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "themes");
        var t = new ThemeLoader(new FolderThemeSource(dir)).Load(id);
        foreach (var key in MatrixStringKeys.All) t.Strings.Has(key).Should().BeTrue($"string '{key}'");
    }
}
