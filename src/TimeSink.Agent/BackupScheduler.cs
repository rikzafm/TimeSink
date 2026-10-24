using TimeSink.Agent.Native;
using TimeSink.Core.Backup;

namespace TimeSink.Agent;

/// <summary>
/// Automatic backups (implementation.md section 10): checks on start and then hourly whether <c>backup.mode</c>
/// says one is due. The snapshot runs on a pool thread (VACUUM INTO is safe next to the tracking writes);
/// <c>backup.lastUtc</c> and retention are handled by <see cref="AutoBackup"/>.
/// </summary>
internal sealed class BackupScheduler
{
    private const nuint TimerId = 2;
    private const uint CheckMs = 60 * 60 * 1000;

    private readonly string _dbPath;
    private readonly nint _hwnd;
    private int _running;

    public BackupScheduler(string dbPath, nint hwnd)
    {
        _dbPath = dbPath;
        _hwnd = hwnd;
    }

    public static string DefaultFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeSink", "backups");

    public void Start()
    {
        NativeMethods.SetTimer(_hwnd, TimerId, CheckMs, 0);
        Check();
    }

    public void Stop() => NativeMethods.KillTimer(_hwnd, TimerId);

    /// <summary>Handles a window message; returns true if consumed.</summary>
    public bool HandleMessage(uint msg, nuint wParam)
    {
        if (msg != NativeMethods.WM_TIMER || wParam != TimerId) return false;
        Check();
        return true;
    }

    private void Check()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1) return;
        Task.Run(() =>
        {
            try
            {
                var version = typeof(BackupScheduler).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
                if (AutoBackup.RunIfDue(_dbPath, DateTimeOffset.UtcNow, DefaultFolder, version) is { } file)
                    Log.Info($"backup: {file}");
            }
            catch (Exception ex) { Log.Error($"backup: {ex.GetType().Name}: {ex.Message}"); }
            finally { Interlocked.Exchange(ref _running, 0); }
        });
    }
}
