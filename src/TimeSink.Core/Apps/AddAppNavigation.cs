namespace TimeSink.Core.Apps;

public enum AddAppTab { Running, Installed, Tracked, Browse }

/// <summary>What the open Tracked row is doing: nothing, being renamed, or asking to stop tracking.</summary>
public enum TrackedRowMode { None, Rename, Stop }

/// <summary>The merge screen: not open, its plan being built on a pool thread, or built and showing.</summary>
public enum MergeStage { None, Opening, Ready }

public enum SettingsAskKind { None, Stop, ChangeMain }

/// <summary>The confirmation open in an app's settings: none, stop tracking, or make a Merged app the Main app.</summary>
public readonly record struct SettingsAsk(SettingsAskKind Kind, long MainId = 0)
{
    public static SettingsAsk None => default;

    public static SettingsAsk Stop => new(SettingsAskKind.Stop);

    public static SettingsAsk ChangeMain(long newMainId) => new(SettingsAskKind.ChangeMain, newMainId);

    public bool IsNone => Kind == SettingsAskKind.None;
}

/// <summary>Everything <see cref="AddAppNavigation"/> remembers about where the user is on the Add app screen.</summary>
public sealed record AddAppState(
    AddAppTab Tab = AddAppTab.Running,
    long? OpenRowId = null,
    TrackedRowMode RowMode = TrackedRowMode.None,
    MergeStage Merge = MergeStage.None,
    long? MergeMainId = null,
    long? SettingsId = null,
    SettingsAsk Ask = default,
    int ClearStep = 0,
    long? SettingsReturnId = null,
    bool ScrollRestorePending = false);

/// <summary>What the Add app screen shows right now. Settings covers the whole flyout; the others fill the tab body.</summary>
public abstract record AddAppScreen
{
    private AddAppScreen() { }

    public sealed record Running : AddAppScreen;

    public sealed record Installed : AddAppScreen;

    public sealed record Browse : AddAppScreen;

    public sealed record Tracked : AddAppScreen;

    public sealed record MergeLoading(long MainId) : AddAppScreen;

    public sealed record Merge(long MainId) : AddAppScreen;

    public sealed record Settings(long Id) : AddAppScreen;
}

/// <summary>
/// Which Add app screen is showing and what is open on it: the tab, the open Tracked row and its mode, the merge screen
/// and the settings screen with its pending question and Clear history step. Views render <see cref="Screen"/> and report
/// what the user did through the intent methods; scroll positions, search text and loaded data stay in the views.
/// </summary>
public sealed class AddAppNavigation
{
    public AddAppState State { get; private set; } = new();

    public AddAppScreen Screen => State switch
    {
        { SettingsId: { } id } => new AddAppScreen.Settings(id),
        { Tab: AddAppTab.Running } => new AddAppScreen.Running(),
        { Tab: AddAppTab.Installed } => new AddAppScreen.Installed(),
        { Tab: AddAppTab.Browse } => new AddAppScreen.Browse(),
        { Merge: MergeStage.Opening, MergeMainId: { } main } => new AddAppScreen.MergeLoading(main),
        { Merge: MergeStage.Ready, MergeMainId: { } main } => new AddAppScreen.Merge(main),
        _ => new AddAppScreen.Tracked(),
    };

    /// <summary>True while the Tracked tab's list (not the merge screen) is the tab body, whether or not settings cover it.</summary>
    public bool ShowsTrackedList => State is { Tab: AddAppTab.Tracked, Merge: MergeStage.None };

    /// <summary>A new visit: drops everything that belonged to the previous one. The tab stays; the view re-selects it.</summary>
    public void Reset() => State = State with
    {
        OpenRowId = null, RowMode = TrackedRowMode.None, Merge = MergeStage.None, MergeMainId = null,
        SettingsId = null, Ask = SettingsAsk.None, ClearStep = 0, SettingsReturnId = null, ScrollRestorePending = false,
    };

    /// <summary>Switching to the Tracked tab starts it fresh: no merge screen, no open row.</summary>
    public void SelectTab(AddAppTab tab) => State = tab == AddAppTab.Tracked
        ? State with { Tab = tab, Merge = MergeStage.None, MergeMainId = null, OpenRowId = null, RowMode = TrackedRowMode.None }
        : State with { Tab = tab };

    // ---- Tracked row --------------------------------------------------------------------------------------

    /// <summary>
    /// A click on a row selects it: any rename or stop question on another row closes. Selecting the open row again changes
    /// nothing, so a click inside its stop question or rename box leaves it alone. Returns true when the selection moved.
    /// </summary>
    public bool SelectRow(long appId)
    {
        if (State.OpenRowId == appId) return false;
        State = State with { OpenRowId = appId, RowMode = TrackedRowMode.None };
        return true;
    }

    public void OpenRowRename(long appId) => State = State with { OpenRowId = appId, RowMode = TrackedRowMode.Rename };

    public void OpenRowStop(long appId) => State = State with { OpenRowId = appId, RowMode = TrackedRowMode.Stop };

    /// <summary>Rename or the stop question is done; the row stays open.</summary>
    public void EndRowMode() => State = State with { RowMode = TrackedRowMode.None };

    public void CloseRow() => State = State with { OpenRowId = null, RowMode = TrackedRowMode.None };

    // ---- merge screen -------------------------------------------------------------------------------------

    public void BeginMergeOpen(long mainId) => State = State with { Merge = MergeStage.Opening, MergeMainId = mainId };

    /// <summary>
    /// The plan for <paramref name="mainId"/> finished building. Returns false (and changes nothing) when the user went
    /// elsewhere meanwhile; otherwise the merge screen is ready, or gone when there is no plan (app gone, database busy).
    /// </summary>
    public bool CompleteMergeOpen(long mainId, bool planReady)
    {
        if (State is not { Merge: MergeStage.Opening, MergeMainId: { } opening } || opening != mainId) return false;
        State = State with { Merge = planReady ? MergeStage.Ready : MergeStage.None, MergeMainId = planReady ? mainId : null };
        return true;
    }

    /// <summary>Cancel on the merge screen: back to the Tracked list where it was, or to the settings screen that opened it.</summary>
    public void CancelMerge()
    {
        State = State with { Merge = MergeStage.None, MergeMainId = null, ScrollRestorePending = true };
        ReturnToSettings();
    }

    /// <summary>The merge was saved: the Main app's row opens in the Tracked list, or the settings screen that opened the merge returns.</summary>
    public void MergeConfirmed(long mainId)
    {
        State = State with { Merge = MergeStage.None, MergeMainId = null, ScrollRestorePending = true, OpenRowId = mainId, RowMode = TrackedRowMode.None };
        ReturnToSettings();
    }

    /// <summary>True once after the merge screen closed, so the Tracked list lands where it was.</summary>
    public bool TakeScrollRestore()
    {
        var pending = State.ScrollRestorePending;
        State = State with { ScrollRestorePending = false };
        return pending;
    }

    // ---- settings screen ----------------------------------------------------------------------------------

    public void OpenSettings(long appId) => State = State with { SettingsId = appId, Ask = SettingsAsk.None, ClearStep = 0 };

    /// <summary>Merge… on the settings screen: the merge screen opens, and closing it brings the settings screen back.</summary>
    public void OpenMergeFromSettings(long mainId)
    {
        State = State with { SettingsReturnId = mainId, SettingsId = null };
        BeginMergeOpen(mainId);
    }

    public void CloseSettings() => State = State with { SettingsId = null, SettingsReturnId = null, Ask = SettingsAsk.None, ClearStep = 0 };

    public void AskStop() => State = State with { Ask = SettingsAsk.Stop };

    public void AskChangeMain(long newMainId) => State = State with { Ask = SettingsAsk.ChangeMain(newMainId) };

    public void CancelAsk() => State = State with { Ask = SettingsAsk.None };

    /// <summary>The Main app changed: the settings screen now belongs to the new Main app.</summary>
    public void ChangeMainDone(long newMainId) => State = State with { Ask = SettingsAsk.None, SettingsId = newMainId };

    public void StartClear() => State = State with { ClearStep = 1 };

    /// <summary>Yes on a Clear history question. Returns true when it was the last (third) one and the history is to be cleared.</summary>
    public bool NextClearStep()
    {
        if (State.ClearStep >= 3) return true;
        State = State with { ClearStep = State.ClearStep + 1 };
        return false;
    }

    public void DismissClear() => State = State with { ClearStep = 0 };

    private void ReturnToSettings()
    {
        if (State.SettingsReturnId is not { } id) return;
        State = State with { SettingsReturnId = null };
        OpenSettings(id);
    }
}
