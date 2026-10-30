using TimeSink.Core.Settings;

namespace TimeSink.Tests.Settings;

public class AutostartTests
{
    private sealed class FakeRunKey : IRunKey
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? Get(string name) => Values.GetValueOrDefault(name);
        public void Set(string name, string value) => Values[name] = value;
        public void Remove(string name) => Values.Remove(name);
    }

    private const string Exe = @"C:\Apps\TimeSink\TimeSink.Agent.exe";

    [Fact]
    public void Enabling_writes_the_quoted_agent_path_with_background_flag()
    {
        var key = new FakeRunKey();
        new Autostart(key, Exe).SetEnabled(true);
        key.Values["TimeSink"].Should().Be("\"C:\\Apps\\TimeSink\\TimeSink.Agent.exe\" --background");
    }

    [Fact]
    public void Disabling_removes_the_entry_and_is_safe_when_absent()
    {
        var key = new FakeRunKey();
        var auto = new Autostart(key, Exe);
        auto.SetEnabled(true);
        auto.IsEnabled.Should().BeTrue();
        auto.SetEnabled(false);
        auto.IsEnabled.Should().BeFalse();
        key.Values.Should().BeEmpty();
        auto.SetEnabled(false);
    }

    [Fact]
    public void Enabling_from_a_moved_install_replaces_the_old_entry()
    {
        var key = new FakeRunKey();
        key.Values["TimeSink"] = "\"D:\\Old\\TimeSink.Agent.exe\" --background";
        new Autostart(key, Exe).SetEnabled(true);
        key.Values.Should().ContainSingle().Which.Value.Should().Contain(@"C:\Apps");
    }

    private sealed class FakeSettings : ISettingsSource
    {
        public Dictionary<string, string> Values { get; } = new();
        public string? Get(string key) => Values.GetValueOrDefault(key);
        public void Set(string key, string value) => Values[key] = value;
    }

    [Fact]
    public void First_run_turns_start_with_windows_on_and_records_it()
    {
        var key = new FakeRunKey();
        var settings = new FakeSettings();
        new Autostart(key, Exe).SeedDefault(settings);
        key.Values.Should().ContainKey("TimeSink");
        settings.Values["startup.enabled"].Should().Be("true");
    }

    [Fact]
    public void A_stored_choice_is_not_overridden_by_the_default()
    {
        var key = new FakeRunKey();
        var settings = new FakeSettings();
        settings.Values["startup.enabled"] = "false";
        new Autostart(key, Exe).SeedDefault(settings);
        key.Values.Should().BeEmpty();
    }

    [Fact]
    public void Other_run_entries_are_untouched()
    {
        var key = new FakeRunKey();
        key.Values["Other"] = "x";
        var auto = new Autostart(key, Exe);
        auto.SetEnabled(true);
        auto.SetEnabled(false);
        key.Values.Should().ContainKey("Other");
    }
}
