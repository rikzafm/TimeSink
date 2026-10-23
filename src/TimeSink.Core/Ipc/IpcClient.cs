using System.IO.Pipes;

namespace TimeSink.Core.Ipc;

/// <summary>
/// UI side of the pipe: connects (retrying while the Agent is not there), asks for the state, then raises
/// <see cref="StateReceived"/> for every push. Events fire on a thread-pool thread.
/// </summary>
public sealed class IpcClient : IDisposable
{
    private readonly string _name;
    private readonly TimeSpan _retry;
    private readonly CancellationTokenSource _cts = new();

    public IpcClient(string pipeName, TimeSpan? retryDelay = null)
    {
        _name = pipeName;
        _retry = retryDelay ?? TimeSpan.FromSeconds(2);
    }

    public event Action<StateMessage>? StateReceived;
    public event Action<bool>? ConnectionChanged;

    public void Start() => _ = Task.Run(Loop);

    private async Task Loop()
    {
        var ct = _cts.Token;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeClientStream(".", _name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.ConnectAsync(500, ct).ConfigureAwait(false);
                ConnectionChanged?.Invoke(true);
                try
                {
                    await IpcFraming.WriteAsync(pipe, new GetStateMessage(), ct).ConfigureAwait(false);
                    while (await IpcFraming.ReadAsync(pipe, ct).ConfigureAwait(false) is { } msg)
                        if (msg is StateMessage s) StateReceived?.Invoke(s);
                }
                finally { ConnectionChanged?.Invoke(false); }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is IOException or TimeoutException or InvalidDataException or UnauthorizedAccessException) { }

            try { await Task.Delay(_retry, ct).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
    }
}
