using TimeSink.Core.Apps;

namespace TimeSink.Agent;

/// <summary>
/// Adds newly installed Steam games to the tracked apps: once at start-up, then whenever Steam writes an app manifest
/// or changes its library list. No polling: a watcher on each library's <c>steamapps</c> folder wakes a scan after the
/// writes settle (a manifest appears when a download starts and is rewritten when it finishes). Scans run on a pool
/// thread, open their own short database connection, and do nothing when the "steam.autoTrack" setting is off or Steam
/// is not installed. The tracking tick picks new apps up on its own reload.
/// </summary>
internal sealed class SteamScanHost : IDisposable
{
    private static readonly TimeSpan Settle = TimeSpan.FromSeconds(30);

    private readonly string _dbPath;
    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private Timer? _debounce;
    private bool _disposed;

    public SteamScanHost(string dbPath) => _dbPath = dbPath;

    public void Start()
    {
        _debounce = new Timer(_ => Scan(), null, Timeout.Infinite, Timeout.Infinite);
        ThreadPool.QueueUserWorkItem(_ => Scan());
    }

    private void Scan()
    {
        try
        {
            var root = SteamLibrary.FindRoot();
            if (root is null) return;
            Watch(root);
            var added = SteamAutoTracker.Run(_dbPath, root, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            if (added > 0) Log.Info($"steam: tracking {added} new game(s)");
        }
        catch (Exception ex) { Log.Error($"steam: {ex.GetType().Name}: {ex.Message}"); }
    }

    /// <summary>(Re)creates one watcher per library folder; called on every scan so a new library gets watched.</summary>
    private void Watch(string root)
    {
        lock (_gate)
        {
            if (_disposed) return;
            foreach (var w in _watchers) w.Dispose();
            _watchers.Clear();
            foreach (var lib in SteamLibrary.LibraryFolders(root))
            {
                var steamapps = Path.Combine(lib, "steamapps");
                if (!Directory.Exists(steamapps)) continue;
                try
                {
                    var w = new FileSystemWatcher(steamapps)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                        EnableRaisingEvents = true,
                    };
                    FileSystemEventHandler changed = (_, e) => OnChange(e.Name);
                    w.Created += changed;
                    w.Changed += changed;
                    w.Renamed += (_, e) => OnChange(e.Name);
                    _watchers.Add(w);
                }
                catch (Exception ex) { Log.Error($"steam watch: {ex.GetType().Name}: {ex.Message}"); }
            }
        }
    }

    private void OnChange(string? name)
    {
        if (name is null) return;
        var manifest = name.StartsWith("appmanifest_", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".acf", StringComparison.OrdinalIgnoreCase);
        if (!manifest && !name.Equals("libraryfolders.vdf", StringComparison.OrdinalIgnoreCase)) return;
        lock (_gate) { if (!_disposed) _debounce?.Change(Settle, Timeout.InfiniteTimeSpan); } // each write pushes the scan back
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var w in _watchers) w.Dispose();
            _watchers.Clear();
            _debounce?.Dispose();
        }
    }
}
