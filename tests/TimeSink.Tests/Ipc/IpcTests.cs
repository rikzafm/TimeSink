using TimeSink.Core.Ipc;

namespace TimeSink.Tests.Ipc;

public class IpcFramingTests
{
    [Fact]
    public async Task A_state_message_round_trips_length_prefixed_utf8_json()
    {
        var ms = new MemoryStream();
        var state = new StateMessage(false, [new RunningApp(7, 1_700_000_000), new RunningApp(9, 1_700_000_500)]);
        await IpcFraming.WriteAsync(ms, state);

        var bytes = ms.ToArray();
        BitConverter.ToInt32(bytes, 0).Should().Be(bytes.Length - 4);
        System.Text.Encoding.UTF8.GetString(bytes, 4, bytes.Length - 4).Should().Contain("\"type\":\"state\"");

        ms.Position = 0;
        var back = (StateMessage)(await IpcFraming.ReadAsync(ms))!;
        back.Paused.Should().BeFalse();
        back.Running.Should().Equal(state.Running);
    }

    [Fact]
    public async Task GetState_round_trips_and_end_of_stream_reads_null()
    {
        var ms = new MemoryStream();
        await IpcFraming.WriteAsync(ms, new GetStateMessage());
        ms.Position = 0;
        (await IpcFraming.ReadAsync(ms)).Should().BeOfType<GetStateMessage>();
        (await IpcFraming.ReadAsync(ms)).Should().BeNull();
    }

    [Fact]
    public async Task An_oversized_frame_is_rejected()
    {
        var ms = new MemoryStream(BitConverter.GetBytes(64 * 1024 * 1024));
        await FluentActions.Awaiting(() => IpcFraming.ReadAsync(ms)).Should().ThrowAsync<InvalidDataException>();
    }
}

public class IpcPipeTests
{
    private static string NewName() => "TimeSink.Test." + Guid.NewGuid().ToString("N");

    private static async Task<T> Next<T>(TaskCompletionSource<T> tcs) => await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

    [Fact]
    public async Task Client_gets_the_current_state_on_connect_and_every_push_after()
    {
        var name = NewName();
        var current = new StateMessage(false, [new RunningApp(1, 100)]);
        using var server = new IpcServer(name, () => current);
        server.Start();

        var first = new TaskCompletionSource<StateMessage>();
        var second = new TaskCompletionSource<StateMessage>();
        using var client = new IpcClient(name);
        client.StateReceived += s => { if (!first.TrySetResult(s)) second.TrySetResult(s); };
        client.Start();

        (await Next(first)).Running.Should().Equal(new RunningApp(1, 100));

        current = new StateMessage(false, [new RunningApp(1, 100), new RunningApp(2, 250)]);
        server.Publish(current);
        (await Next(second)).Running.Should().HaveCount(2);
    }

    [Fact]
    public async Task Client_reconnects_after_the_server_restarts()
    {
        var name = NewName();
        var got = new TaskCompletionSource<StateMessage>();
        using var client = new IpcClient(name, TimeSpan.FromMilliseconds(50));
        client.StateReceived += s => got.TrySetResult(s);
        client.Start(); // server not there yet

        using var server = new IpcServer(name, () => new StateMessage(true, []));
        server.Start();
        (await Next(got)).Paused.Should().BeTrue();
    }

    [Fact]
    public async Task A_set_icon_request_from_the_UI_reaches_the_agent()
    {
        var name = NewName();
        using var server = new IpcServer(name, () => new StateMessage(false, []));
        var got = new TaskCompletionSource<string>();
        server.IconRequested += id => got.TrySetResult(id);
        server.Start();

        await IpcClient.SendAsync(name, new SetIconMessage("misprint.rings"));

        (await Next(got)).Should().Be("misprint.rings");
    }

    [Fact]
    public async Task Sending_with_no_agent_listening_does_not_throw()
        => await FluentActions.Awaiting(() => IpcClient.SendAsync(NewName(), new SetIconMessage("clean.bars"), TimeSpan.FromMilliseconds(50)))
            .Should().NotThrowAsync();

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void A_scratch_database_gets_its_own_pipe_name()
    {
        PipeNames.For(null).Should().Be(PipeNames.ForCurrentUser());
        PipeNames.For(@"C:\Tmp\A.db").Should().NotBe(PipeNames.ForCurrentUser());
        PipeNames.For(@"C:\Tmp\A.db").Should().Be(PipeNames.For(@"c:\tmp\a.db"));
        PipeNames.For(@"C:\Tmp\A.db").Should().NotBe(PipeNames.For(@"C:\Tmp\B.db"));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Pipe_name_is_per_user()
    {
        PipeNames.ForCurrentUser().Should().StartWith("TimeSink.").And.NotBe("TimeSink.");
    }
}
