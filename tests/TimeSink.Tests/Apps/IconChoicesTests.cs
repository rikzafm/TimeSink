using System.Text;
using TimeSink.Core.Apps;

namespace TimeSink.Tests.Apps;

/// <summary>The icons offered for an app: its own exe, the other exes next to it, Steam art, and a picked file (decision 152).</summary>
public sealed class IconChoicesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "timesink-icons-" + Guid.NewGuid().ToString("N"));

    public IconChoicesTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    private sealed class FakeDesktop : IDesktop
    {
        public HashSet<string> Same { get; } = new(StringComparer.OrdinalIgnoreCase);
        public WindowCapture CaptureWindows() => new([], new Dictionary<string, string>());
        public IReadOnlyList<InstalledApp> ScanInstalled() => [];

        public byte[]? ExtractIcon(string exePath, string? aumid, string? imageFile, int size) =>
            imageFile is not null ? Encoding.UTF8.GetBytes("art:" + imageFile)
            : Encoding.UTF8.GetBytes(Same.Contains(exePath) ? "same" : "exe:" + exePath);
    }

    private string Exe(string name, int bytes)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    [Fact]
    public void The_apps_own_icon_comes_first_then_the_larger_exes_beside_it_without_helpers()
    {
        var launcher = Exe("launcher.exe", 10);
        var game = Exe("game.exe", 500);
        Exe("unins000.exe", 900);

        var found = IconChoices.Find(launcher, new FakeDesktop(), 64);

        found.Select(c => (c.Kind, c.Detail)).Should().Equal((IconChoiceKind.App, "launcher.exe"), (IconChoiceKind.Exe, "game.exe"));
        found[1].Png.Should().Equal(Encoding.UTF8.GetBytes("exe:" + game));
    }

    [Fact]
    public void An_exe_that_draws_the_same_picture_is_listed_once()
    {
        var launcher = Exe("launcher.exe", 10);
        var game = Exe("game.exe", 500);
        var desktop = new FakeDesktop();
        desktop.Same.Add(launcher);
        desktop.Same.Add(game);

        IconChoices.Find(launcher, desktop, 64).Should().ContainSingle().Which.Kind.Should().Be(IconChoiceKind.App);
    }

    [Fact]
    public void An_app_alone_in_its_folder_has_one_choice_so_no_picker_is_offered()
    {
        IconChoices.Find(Exe("solo.exe", 10), new FakeDesktop(), 64).Should().HaveCount(1);
    }

    [Fact]
    public void A_picked_image_file_becomes_a_file_choice()
    {
        var choice = IconChoices.FromFile(@"c:\pics\mine.png", new FakeDesktop(), 64);

        choice.Should().NotBeNull();
        choice!.Kind.Should().Be(IconChoiceKind.File);
        choice.Detail.Should().Be("mine.png");
    }
}
