using Microsoft.Data.Sqlite;
using TimeSink.Core.Usage;

namespace TimeSink.Tests.Usage;

public sealed class UsageSnapshotLoaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 3, 4, 12, 0, 0, TimeSpan.Zero);

    private static UsageSnapshotLoader Loader(IReadOnlyCollection<long>? running = null, Func<long?>? signIn = null, Func<WeekStart>? weekStart = null) =>
        new(() => Now, TimeZoneInfo.Utc, () => running, signIn ?? (() => null), weekStart ?? (() => WeekStart.Mon));

    [Fact]
    public async Task Refresh_does_nothing_until_started()
    {
        var loader = Loader();
        var queried = false;

        var applied = await loader.Refresh(_ => queried = true, _ => { });

        applied.Should().BeFalse();
        queried.Should().BeFalse();
    }

    [Fact]
    public async Task Refresh_applies_the_query_result_with_the_context_captured_at_the_call()
    {
        var running = new HashSet<long> { 1, 2 };
        var loader = Loader(running);
        loader.Start();
        UsageContext? seen = null;
        var results = new List<int>();

        var applied = await loader.Refresh(c => { seen = c; return 42; }, results.Add);

        applied.Should().BeTrue();
        results.Should().Equal(42);
        seen!.Now.Should().Be(Now);
        seen.Zone.Should().Be(TimeZoneInfo.Utc);
        seen.Running.Should().BeSameAs(running);
    }

    [Fact]
    public async Task A_newer_refresh_overtakes_an_older_one_that_finishes_later()
    {
        var loader = Loader();
        loader.Start();
        using var release = new ManualResetEventSlim();
        var applied = new List<string>();

        var first = loader.Refresh(_ => { release.Wait(); return "old"; }, applied.Add);
        var second = await loader.Refresh(_ => "new", applied.Add);
        release.Set();
        var firstApplied = await first;

        second.Should().BeTrue();
        firstApplied.Should().BeFalse();
        applied.Should().Equal("new");
    }

    [Fact]
    public async Task Stopping_while_a_query_runs_drops_its_result()
    {
        var loader = Loader();
        loader.Start();
        using var release = new ManualResetEventSlim();
        var applied = new List<int>();

        var pending = loader.Refresh(_ => { release.Wait(); return 1; }, applied.Add);
        loader.Stop();
        release.Set();

        (await pending).Should().BeFalse();
        applied.Should().BeEmpty();
    }

    [Theory]
    [InlineData(typeof(SqliteException))]
    [InlineData(typeof(IOException))]
    public async Task A_missing_or_locked_database_drops_the_result_without_throwing(Type failure)
    {
        var loader = Loader();
        loader.Start();
        var applied = new List<int>();

        var result = await loader.Refresh<int>(_ => throw (Exception)(failure == typeof(SqliteException) ? new SqliteException("no db", 14) : new IOException("locked")), applied.Add);

        result.Should().BeFalse();
        applied.Should().BeEmpty();
    }

    [Fact]
    public async Task Other_failures_are_not_swallowed()
    {
        var loader = Loader();
        loader.Start();

        var act = () => loader.Refresh<int>(_ => throw new InvalidOperationException("bug"), _ => { });

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Sign_in_and_week_start_are_looked_up_only_when_a_query_uses_them_and_only_once()
    {
        var signInCalls = 0;
        var weekStartCalls = 0;
        var loader = Loader(signIn: () => { signInCalls++; return 1000; }, weekStart: () => { weekStartCalls++; return WeekStart.Sun; });
        loader.Start();

        await loader.Refresh(_ => 0, _ => { });
        signInCalls.Should().Be(0);
        weekStartCalls.Should().Be(0);

        long? signIn = null;
        DayOfWeek day = default;
        await loader.Refresh(c => (c.SignInUtc, c.SignInUtc, c.WeekStartDay, c.WeekStart), t => (signIn, day) = (t.Item1, t.Item3));

        signIn.Should().Be(1000);
        day.Should().Be(DayOfWeek.Sunday);
        signInCalls.Should().Be(1);
        weekStartCalls.Should().Be(1);
    }

    [Fact]
    public async Task Start_and_stop_are_idempotent_and_a_restarted_loader_refreshes_again()
    {
        var loader = Loader();
        loader.Start();
        loader.Start();
        loader.Started.Should().BeTrue();
        loader.Stop();
        loader.Stop();
        loader.Started.Should().BeFalse();
        loader.Start();

        (await loader.Refresh(_ => 1, _ => { })).Should().BeTrue();
    }
}
