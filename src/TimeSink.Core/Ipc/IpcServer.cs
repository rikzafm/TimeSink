using System.IO.Pipes;

namespace TimeSink.Core.Ipc;

/// <summary>
/// Agent side of the pipe. Accepts any number of UI clients (current user only), answers <c>getState</c> and fans out
/// <see cref="Publish"/>. Writes are fire-and-forget so a stuck client can never block the Agent's message loop.
/// </summary>
public sealed class IpcServer : IDisposable
{
    private sealed class Client(NamedPipeServerStream pipe)
    {
        public readonly NamedPipeServerStream Pipe = pipe;
        public readonly SemaphoreSlim WriteLock = new(1, 1);
    }

    private readonly string _name;
    private readonly Func<StateMessage> _current;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Client> _clients = new();
    private readonly Action<string>? _log;

    public IpcServer(string pipeName, Func<StateMessage> currentState, Action<string>? log = null)
    {
        _name = pipeName;
        _current = currentState;
        _log = log;
    }

    /// <summary>Raised (on a thread-pool thread) with the icon id when a UI sends <see cref="SetIconMessage"/>.</summary>
    public event Action<string>? IconRequested;

    public void Start() => _ = Task.Run(AcceptLoop);

    public int ClientCount { get { lock (_clients) return _clients.Count; } }

    public void Publish(StateMessage state)
    {
        Client[] snapshot;
        lock (_clients) snapshot = _clients.ToArray();
        foreach (var c in snapshot) _ = SendAsync(c, state);
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(_name, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                var client = new Client(pipe);
                lock (_clients) _clients.Add(client);
                _ = Task.Run(() => Serve(client));
            }
            catch (OperationCanceledException) { pipe?.Dispose(); return; }
            catch (Exception ex)
            {
                pipe?.Dispose();
                _log?.Invoke($"ipc accept: {ex.GetType().Name}: {ex.Message}");
                try { await Task.Delay(1000, _cts.Token).ConfigureAwait(false); } catch (OperationCanceledException) { return; }
            }
        }
    }

    private async Task Serve(Client client)
    {
        try
        {
            while (await IpcFraming.ReadAsync(client.Pipe, _cts.Token).ConfigureAwait(false) is { } msg)
            {
                if (msg is GetStateMessage) await SendAsync(client, _current()).ConfigureAwait(false);
                else if (msg is SetIconMessage icon) IconRequested?.Invoke(icon.IconId);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            lock (_clients) _clients.Remove(client);
            client.Pipe.Dispose();
        }
    }

    private async Task SendAsync(Client client, StateMessage state)
    {
        try
        {
            await client.WriteLock.WaitAsync(_cts.Token).ConfigureAwait(false);
            try { await IpcFraming.WriteAsync(client.Pipe, state, _cts.Token).ConfigureAwait(false); }
            finally { client.WriteLock.Release(); }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { client.Pipe.Dispose(); }
    }

    public void Dispose()
    {
        _cts.Cancel();
        lock (_clients)
        {
            foreach (var c in _clients) c.Pipe.Dispose();
            _clients.Clear();
        }
    }
}
