using TimeSink.Core.Backup;

namespace TimeSink.Tests.Backup;

public sealed class DataLifecycleTests : IDisposable
{
    private const long T0 = 1_700_000_000;
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(T0 + 100_000);

    private readonly BackupTestRig _rig = new();
    private readonly RecordingSignal _agent = new();

    public void Dispose() => _rig.Dispose();

    private sealed class RecordingSignal : IAgentSignal
    {
        public int Resets { get; private set; }
        public void Reset() => Resets++;
    }

    private DataLifecycle Over(string db) => new(db, _agent);

    /// <summary>A backup file of a database holding one app and one session.</summary>
    private string BackupOfOneSession()
    {
        var source = _rig.NewDb("source.db", (@"c:\a\one.exe", T0, T0 + 600));
        var file = _rig.P("source.timesink");
        BackupService.Create(source, file, Now);
        return file;
    }

    [Fact]
    public void Replace_swaps_the_content_and_tells_the_agent_once()
    {
        var file = BackupOfOneSession();
        var live = _rig.NewDb("live.db", (@"c:\b\two.exe", T0, T0 + 60));

        var result = Over(live).ReplaceFromBackup(file, _rig.P("backups"), Now, TimeZoneInfo.Utc);

        result.Succeeded.Should().BeTrue();
        result.Value!.SafetyCopy.Should().NotBeNull();
        _rig.Rows(live, "SELECT exe_path FROM apps").Should().Equal(@"c:\a\one.exe");
        _agent.Resets.Should().Be(1);
    }

    [Fact]
    public void Replace_from_an_unusable_file_fails_with_a_message_and_leaves_the_agent_alone()
    {
        var live = _rig.NewDb("live.db", (@"c:\b\two.exe", T0, T0 + 60));
        var before = _rig.Fingerprint(live);
        var junk = _rig.P("junk.timesink");
        File.WriteAllText(junk, "not a backup");

        var result = Over(live).ReplaceFromBackup(junk, _rig.P("backups"), Now, TimeZoneInfo.Utc);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        _rig.Fingerprint(live).Should().Be(before);
        _agent.Resets.Should().Be(0);
    }

    [Fact]
    public void Merge_adds_rows_without_telling_the_agent()
    {
        var file = BackupOfOneSession();
        var live = _rig.NewDb("live.db", (@"c:\b\two.exe", T0, T0 + 60));

        var result = Over(live).Merge(file, TimeZoneInfo.Utc);

        result.Succeeded.Should().BeTrue();
        result.Value!.SessionsAdded.Should().Be(1);
        _rig.Rows(live, "SELECT exe_path FROM apps ORDER BY exe_path").Should().Equal(@"c:\a\one.exe", @"c:\b\two.exe");
        _agent.Resets.Should().Be(0);
    }

    [Fact]
    public void Inspect_reports_what_is_in_the_backup_and_fails_on_a_file_that_is_not_one()
    {
        var file = BackupOfOneSession();
        var live = _rig.NewDb("live.db");

        var good = Over(live).Inspect(file);
        good.Succeeded.Should().BeTrue();
        good.Value!.Apps.Should().Be(1);
        good.Value.Sessions.Should().Be(1);

        var junk = _rig.P("junk.timesink");
        File.WriteAllText(junk, "not a backup");
        var bad = Over(live).Inspect(junk);
        bad.Succeeded.Should().BeFalse();
        bad.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Delete_all_history_clears_the_history_and_tells_the_agent_once()
    {
        var live = _rig.NewDb("live.db", (@"c:\a\one.exe", T0, T0 + 600));

        var result = Over(live).DeleteAllHistory("DELETE");

        result.Succeeded.Should().BeTrue();
        _rig.Scalar<long>(live, "SELECT COUNT(*) FROM sessions").Should().Be(0);
        _rig.Scalar<long>(live, "SELECT COUNT(*) FROM apps").Should().Be(1);
        _agent.Resets.Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("delete")]
    [InlineData("DELETE ")]
    public void Delete_all_history_with_the_wrong_word_fails_and_changes_nothing(string typed)
    {
        var live = _rig.NewDb("live.db", (@"c:\a\one.exe", T0, T0 + 600));
        var before = _rig.Fingerprint(live);

        var result = Over(live).DeleteAllHistory(typed);

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Contain("DELETE");
        _rig.Fingerprint(live).Should().Be(before);
        _agent.Resets.Should().Be(0);
    }

    [Fact]
    public void Back_up_now_returns_the_file_and_fails_when_the_folder_cannot_be_made()
    {
        var live = _rig.NewDb("live.db", (@"c:\a\one.exe", T0, T0 + 600));

        var good = Over(live).BackUpNow(Now, _rig.P("backups"), "1.0.0");
        good.Succeeded.Should().BeTrue();
        File.Exists(good.Value).Should().BeTrue();

        var blocker = _rig.P("blocker");
        File.WriteAllText(blocker, "a file where a folder should be");
        var bad = Over(live).BackUpNow(Now, blocker, "1.0.0");
        bad.Succeeded.Should().BeFalse();
        bad.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Exports_succeed_and_fail_without_throwing()
    {
        var live = _rig.NewDb("live.db", (@"c:\a\one.exe", T0, T0 + 600));
        var blocker = _rig.P("blocker");
        File.WriteAllText(blocker, "a file");

        Over(live).ExportJson(_rig.P("all.json")).Succeeded.Should().BeTrue();
        Over(live).ExportJson(Path.Combine(blocker, "all.json")).Succeeded.Should().BeFalse();

        var csv = Over(live).ExportCsv(_rig.P("csv"));
        csv.Succeeded.Should().BeTrue();
        File.Exists(csv.Value!.SessionsCsv).Should().BeTrue();
        Over(live).ExportCsv(blocker).Succeeded.Should().BeFalse();
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void The_named_event_signal_sets_a_waiting_event_and_does_nothing_when_nobody_waits()
    {
        var name = @"Local\TimeSink.Tests." + Guid.NewGuid().ToString("N");
        new NamedEventAgentSignal(name).Reset(); // nobody waiting: no exception

        using var waiting = new EventWaitHandle(false, EventResetMode.ManualReset, name);
        new NamedEventAgentSignal(name).Reset();
        waiting.WaitOne(0).Should().BeTrue();
    }
}
