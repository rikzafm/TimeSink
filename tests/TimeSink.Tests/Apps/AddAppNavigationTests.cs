using TimeSink.Core.Apps;

namespace TimeSink.Tests.Apps;

public sealed class AddAppNavigationTests
{
    private static AddAppNavigation OnTracked()
    {
        var nav = new AddAppNavigation();
        nav.SelectTab(AddAppTab.Tracked);
        return nav;
    }

    [Fact]
    public void Starts_on_the_Running_tab()
    {
        new AddAppNavigation().Screen.Should().Be(new AddAppScreen.Running());
    }

    [Fact]
    public void Reset_drops_everything_of_the_previous_visit_but_keeps_the_tab()
    {
        var nav = OnTracked();
        nav.OpenRowStop(1);
        nav.OpenSettings(2);
        nav.AskChangeMain(3);
        nav.StartClear();
        nav.OpenMergeFromSettings(2);
        nav.CancelMerge();
        nav.OpenMergeFromSettings(2);

        nav.Reset();

        nav.State.Should().Be(new AddAppState(Tab: AddAppTab.Tracked));
        nav.Screen.Should().Be(new AddAppScreen.Tracked());
    }

    [Fact]
    public void Switching_to_the_Tracked_tab_drops_an_open_merge_and_an_open_row()
    {
        var nav = OnTracked();
        nav.OpenRowRename(1);
        nav.BeginMergeOpen(1);
        nav.SelectTab(AddAppTab.Browse);

        nav.SelectTab(AddAppTab.Tracked);

        nav.State.Merge.Should().Be(MergeStage.None);
        nav.State.OpenRowId.Should().BeNull();
        nav.State.RowMode.Should().Be(TrackedRowMode.None);
        nav.Screen.Should().Be(new AddAppScreen.Tracked());
    }

    [Fact]
    public void Switching_to_another_tab_leaves_the_Tracked_row_and_merge_alone()
    {
        var nav = OnTracked();
        nav.OpenRowStop(1);
        nav.BeginMergeOpen(1);

        nav.SelectTab(AddAppTab.Installed);

        nav.Screen.Should().Be(new AddAppScreen.Installed());
        nav.State.OpenRowId.Should().Be(1);
        nav.State.Merge.Should().Be(MergeStage.Opening);
    }

    [Fact]
    public void Only_one_Tracked_row_is_open_at_a_time()
    {
        var nav = OnTracked();
        nav.OpenRowRename(1);

        nav.OpenRowStop(2);

        nav.State.OpenRowId.Should().Be(2);
        nav.State.RowMode.Should().Be(TrackedRowMode.Stop);
    }

    [Fact]
    public void Ending_a_row_mode_keeps_the_row_open_and_closing_the_row_closes_it()
    {
        var nav = OnTracked();
        nav.OpenRowRename(1);

        nav.EndRowMode();
        nav.State.OpenRowId.Should().Be(1);
        nav.State.RowMode.Should().Be(TrackedRowMode.None);

        nav.CloseRow();
        nav.State.OpenRowId.Should().BeNull();
    }

    [Fact]
    public void A_stale_merge_result_is_ignored()
    {
        var nav = OnTracked();
        nav.BeginMergeOpen(1);
        nav.BeginMergeOpen(2);

        nav.CompleteMergeOpen(1, planReady: true).Should().BeFalse();
        nav.Screen.Should().Be(new AddAppScreen.MergeLoading(2));

        nav.CompleteMergeOpen(2, planReady: true).Should().BeTrue();
        nav.Screen.Should().Be(new AddAppScreen.Merge(2));
    }

    [Fact]
    public void A_merge_result_after_the_user_went_elsewhere_is_ignored()
    {
        var nav = OnTracked();
        nav.BeginMergeOpen(1);
        nav.SelectTab(AddAppTab.Tracked); // the tab was re-selected: the merge screen is gone

        nav.CompleteMergeOpen(1, planReady: true).Should().BeFalse();
        nav.Screen.Should().Be(new AddAppScreen.Tracked());
    }

    [Fact]
    public void A_merge_without_a_plan_falls_back_to_the_Tracked_list()
    {
        var nav = OnTracked();
        nav.BeginMergeOpen(1);

        nav.CompleteMergeOpen(1, planReady: false).Should().BeTrue();

        nav.Screen.Should().Be(new AddAppScreen.Tracked());
        nav.State.MergeMainId.Should().BeNull();
    }

    [Fact]
    public void The_merge_screen_shows_only_on_the_Tracked_tab()
    {
        var nav = OnTracked();
        nav.BeginMergeOpen(1);
        nav.CompleteMergeOpen(1, true);
        nav.SelectTab(AddAppTab.Browse);

        nav.Screen.Should().Be(new AddAppScreen.Browse());
    }

    [Fact]
    public void Cancelling_a_merge_opened_from_the_Tracked_tab_lands_on_the_list_and_restores_its_scroll_once()
    {
        var nav = OnTracked();
        nav.BeginMergeOpen(1);
        nav.CompleteMergeOpen(1, true);

        nav.CancelMerge();

        nav.Screen.Should().Be(new AddAppScreen.Tracked());
        nav.TakeScrollRestore().Should().BeTrue();
        nav.TakeScrollRestore().Should().BeFalse();
    }

    [Fact]
    public void Cancelling_a_merge_opened_from_settings_returns_to_those_settings()
    {
        var nav = OnTracked();
        nav.OpenSettings(7);
        nav.OpenMergeFromSettings(7);
        nav.Screen.Should().Be(new AddAppScreen.MergeLoading(7));
        nav.CompleteMergeOpen(7, true);
        nav.Screen.Should().Be(new AddAppScreen.Merge(7));

        nav.CancelMerge();

        nav.Screen.Should().Be(new AddAppScreen.Settings(7));
        nav.State.SettingsReturnId.Should().BeNull();
        nav.State.Ask.IsNone.Should().BeTrue();
        nav.State.ClearStep.Should().Be(0);
    }

    [Fact]
    public void A_saved_merge_opens_the_Main_apps_row_and_returns_to_settings_when_they_opened_it()
    {
        var nav = OnTracked();
        nav.OpenRowStop(9);
        nav.OpenSettings(7);
        nav.OpenMergeFromSettings(7);
        nav.CompleteMergeOpen(7, true);

        nav.MergeConfirmed(7);

        nav.State.OpenRowId.Should().Be(7);
        nav.State.RowMode.Should().Be(TrackedRowMode.None);
        nav.Screen.Should().Be(new AddAppScreen.Settings(7));
        nav.TakeScrollRestore().Should().BeTrue();
    }

    [Fact]
    public void A_saved_merge_from_the_Tracked_tab_stays_on_the_list()
    {
        var nav = OnTracked();
        nav.BeginMergeOpen(4);
        nav.CompleteMergeOpen(4, true);

        nav.MergeConfirmed(4);

        nav.Screen.Should().Be(new AddAppScreen.Tracked());
        nav.State.OpenRowId.Should().Be(4);
    }

    [Fact]
    public void Clear_history_needs_three_confirms_and_stays_open_until_dismissed()
    {
        var nav = OnTracked();
        nav.OpenSettings(1);
        nav.StartClear();
        nav.State.ClearStep.Should().Be(1);

        nav.NextClearStep().Should().BeFalse();
        nav.NextClearStep().Should().BeFalse();
        nav.State.ClearStep.Should().Be(3);
        nav.NextClearStep().Should().BeTrue();
        nav.State.ClearStep.Should().Be(3); // the history is cleared by the caller; a busy database leaves the modal up

        nav.DismissClear();
        nav.State.ClearStep.Should().Be(0);
    }

    [Fact]
    public void Dismissing_the_clear_question_at_any_step_resets_it()
    {
        var nav = OnTracked();
        nav.OpenSettings(1);
        nav.StartClear();
        nav.NextClearStep();

        nav.DismissClear();
        nav.StartClear();

        nav.State.ClearStep.Should().Be(1);
    }

    [Fact]
    public void Changing_the_Main_app_asks_first_and_then_moves_the_settings_to_the_new_Main_app()
    {
        var nav = OnTracked();
        nav.OpenSettings(1);

        nav.AskChangeMain(5);
        nav.State.Ask.Should().Be(SettingsAsk.ChangeMain(5));

        nav.ChangeMainDone(5);
        nav.State.Ask.IsNone.Should().BeTrue();
        nav.Screen.Should().Be(new AddAppScreen.Settings(5));
    }

    [Fact]
    public void Cancelling_a_question_clears_it_and_opening_settings_starts_without_one()
    {
        var nav = OnTracked();
        nav.OpenSettings(1);
        nav.AskStop();
        nav.State.Ask.Should().Be(SettingsAsk.Stop);

        nav.CancelAsk();
        nav.State.Ask.IsNone.Should().BeTrue();

        nav.AskStop();
        nav.StartClear();
        nav.OpenSettings(2);
        nav.State.Ask.IsNone.Should().BeTrue();
        nav.State.ClearStep.Should().Be(0);
    }

    [Fact]
    public void Closing_settings_forgets_the_return_the_question_and_the_clear_step()
    {
        var nav = OnTracked();
        nav.OpenSettings(1);
        nav.AskStop();
        nav.StartClear();

        nav.CloseSettings();

        nav.Screen.Should().Be(new AddAppScreen.Tracked());
        nav.State.Should().Be(new AddAppState(Tab: AddAppTab.Tracked));
    }

    [Fact]
    public void Settings_cover_the_tab_and_the_Tracked_list_still_counts_as_the_body_behind_them()
    {
        var nav = OnTracked();
        nav.OpenSettings(1);

        nav.Screen.Should().Be(new AddAppScreen.Settings(1));
        nav.ShowsTrackedList.Should().BeTrue();

        nav.OpenMergeFromSettings(1);
        nav.ShowsTrackedList.Should().BeFalse();
    }
}
