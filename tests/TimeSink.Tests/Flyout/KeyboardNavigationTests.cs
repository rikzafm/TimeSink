using FluentAssertions;
using TimeSink.Core.Apps;
using TimeSink.Core.Theming;
using TimeSink.Core.Usage;

namespace TimeSink.Tests.Flyout;

public class KeyboardNavigationTests
{
    [Theory]
    [InlineData(0, true, 1)]
    [InlineData(4, true, 0)]
    [InlineData(1, false, 0)]
    [InlineData(0, false, 4)]
    public void CycleTab_computes_correct_next_tab_index(int current, bool forward, int expected)
    {
        var next = forward ? (current + 1) % 5 : (current + 4) % 5;
        next.Should().Be(expected);
    }

    [Theory]
    [InlineData(1, ShellTab.Today)]
    [InlineData(2, ShellTab.Week)]
    [InlineData(3, ShellTab.Month)]
    [InlineData(4, ShellTab.Year)]
    [InlineData(5, ShellTab.All)]
    public void NumberKey_maps_to_expected_ShellTab(int digit, ShellTab expected)
    {
        var tab = (ShellTab)(digit - 1);
        tab.Should().Be(expected);
    }

    [Fact]
    public void AddAppNavigation_unwinds_subview_hierarchy_correctly()
    {
        var nav = new AddAppNavigation();
        nav.SelectTab(AddAppTab.Tracked);

        // 1. Open a row
        nav.SelectRow(42);
        nav.State.OpenRowId.Should().Be(42);

        // Unwind row
        nav.CloseRow();
        nav.State.OpenRowId.Should().BeNull();

        // 2. Open Settings and advance clear step
        nav.OpenSettings(42);
        nav.State.SettingsId.Should().Be(42);
        nav.StartClear();
        nav.State.ClearStep.Should().Be(1);

        // Dismiss clear step
        nav.DismissClear();
        nav.State.ClearStep.Should().Be(0);
        nav.State.SettingsId.Should().Be(42);

        // Close settings
        nav.CloseSettings();
        nav.State.SettingsId.Should().BeNull();
    }

    [Fact]
    public void AddAppNavigation_unwinds_merge_screen_correctly()
    {
        var nav = new AddAppNavigation();
        nav.SelectTab(AddAppTab.Tracked);
        nav.OpenSettings(100);
        nav.OpenMergeFromSettings(100);

        nav.State.Merge.Should().Be(MergeStage.Opening);

        // Canceling merge returns to settings
        nav.CancelMerge();
        nav.State.Merge.Should().Be(MergeStage.None);
    }

    [Fact]
    public void SettingsCatalogue_includes_keyboardShortcuts_in_General_section()
    {
        var row = TimeSink.Core.Settings.SettingsCatalogue.InSection(TimeSink.Core.Settings.SettingsCatalogue.General)
            .FirstOrDefault(r => r.Id == "keyboardShortcuts");

        row.Should().NotBeNull();
        row!.Kind.Should().Be(TimeSink.Core.Settings.SettingKind.Button);
        row.Label.Should().Be("Keyboard shortcuts");
    }

    [Fact]
    public void DateNavigation_steps_previous_and_next_correctly()
    {
        var today = new DateOnly(2026, 11, 2);
        var navigator = new PeriodNavigator(PeriodUnit.Day, new DateOnly(2026, 1, 1), today);

        var prev = navigator.Step(today, -1);
        prev.Should().Be(new DateOnly(2026, 11, 1));

        var next = navigator.Step(prev, 1);
        next.Should().Be(today);
    }
}

