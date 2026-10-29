using System.Windows;
using TimeSink.Core.Ipc;

namespace TimeSink.UI.Services;

/// <summary>
/// The UI's view of the Agent: the running-apps list pushed over the named pipe. <see cref="RunningIds"/> is null until
/// the first state arrives (views then trust the database). <see cref="Changed"/> fires on the UI thread.
/// </summary>
public sealed class LiveState : IDisposable
{
    private readonly IpcClient _client;
    private volatile IReadOnlyCollection<long>? _running;
    private bool _started;

    public LiveState(string pipeName)
    {
        _client = new IpcClient(pipeName);
        _client.StateReceived += OnState;
    }

    public IReadOnlyCollection<long>? RunningIds => _running;

    /// <summary>True while the Games filter is on (set by the header button, remembered in settings); every view reads it when it queries.</summary>
    public bool GamesOnly { get; set; }

    public event Action? Changed;

    /// <summary>Connects (idempotent). Called once the first frame is on screen so it never delays first paint.</summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        _client.Start();
    }

    private void OnState(StateMessage s)
    {
        var ids = s.Running.Select(r => r.AppId).ToHashSet();
        var old = _running;
        _running = ids;
        if (old is not null && old.Count == ids.Count && ids.SetEquals(old)) return;
        Application.Current?.Dispatcher.BeginInvoke(() => Changed?.Invoke());
    }

    /// <summary>Tells the views to re-query after the UI itself changed the database (merge, main app, stop tracking).</summary>
    public void NotifyDataChanged() => Changed?.Invoke();

    public void Dispose() => _client.Dispose();
}
