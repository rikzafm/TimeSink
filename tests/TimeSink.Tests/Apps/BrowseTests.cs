using TimeSink.Core.Apps;

namespace TimeSink.Tests.Apps;

public sealed class BrowseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-browse-" + Guid.NewGuid().ToString("N"));

    public BrowseTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string File_(string name)
    {
        var p = Path.Combine(_dir, name);
        File.WriteAllText(p, "x");
        return p;
    }

    [Fact]
    public void A_dropped_exe_becomes_an_app_named_after_the_file_when_it_has_no_description()
    {
        var exe = File_("MyGame.exe");
        var r = BrowseTarget.Resolve(exe, _ => null);

        r.Error.Should().BeNull();
        r.App.Should().Be(new NewApp(exe, "MyGame", null));
    }

    [Fact]
    public void A_dropped_shortcut_is_followed_to_its_exe_target()
    {
        var target = File_("Real.exe");
        var lnk = File_("Shortcut to Real.lnk");
        var r = BrowseTarget.Resolve(lnk, p => p == lnk ? target : null);

        r.App.Should().Be(new NewApp(target, "Real", null));
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("page.url")]
    public void Other_file_types_are_refused(string name)
    {
        var r = BrowseTarget.Resolve(File_(name), _ => null);
        r.App.Should().BeNull();
        r.Error.Should().Be(BrowseError.NotAnApp);
    }

    [Fact]
    public void A_shortcut_to_a_missing_or_non_exe_target_and_a_missing_file_are_refused()
    {
        var lnk = File_("a.lnk");
        BrowseTarget.Resolve(lnk, _ => Path.Combine(_dir, "gone.exe")).Error.Should().Be(BrowseError.TargetMissing);
        BrowseTarget.Resolve(lnk, _ => null).Error.Should().Be(BrowseError.NotAnApp);
        BrowseTarget.Resolve(lnk, _ => File_("doc.pdf")).Error.Should().Be(BrowseError.NotAnApp);
        BrowseTarget.Resolve(Path.Combine(_dir, "nope.exe"), _ => null).Error.Should().Be(BrowseError.TargetMissing);
    }

    [Fact]
    public void Common_places_list_the_four_folders_in_order_and_flag_the_ones_that_exist()
    {
        var env = new Dictionary<string, string>
        {
            ["ProgramFiles"] = _dir,
            ["ProgramFiles(x86)"] = Path.Combine(_dir, "missing86"),
            ["LocalAppData"] = _dir,
        };
        var places = CommonPlaces.All(k => env.GetValueOrDefault(k));

        places.Select(p => p.Name).Should().Equal("Program Files", "Program Files (x86)", "LocalAppData\\Programs", "WindowsApps");
        places[0].Path.Should().Be(_dir);
        places[1].Exists.Should().BeFalse();
        places[2].Path.Should().Be(Path.Combine(_dir, "Programs"));
        places[3].Path.Should().Be(Path.Combine(_dir, "WindowsApps"));
    }
}
