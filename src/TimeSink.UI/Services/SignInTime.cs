using System.Diagnostics;

namespace TimeSink.UI.Services;

/// <summary>Best-effort sign-in time: the earliest <c>explorer.exe</c> start in this Windows session (null when unknown).</summary>
internal static class SignInTime
{
    private static long? _cached;
    private static bool _looked;

    /// <summary>The sign-in time, looked up once (it never changes within a session).</summary>
    public static long? Cached()
    {
        if (!_looked) { _cached = Get(); _looked = true; }
        return _cached;
    }

    public static long? Get()
    {
        try
        {
            var session = Process.GetCurrentProcess().SessionId;
            long? best = null;
            foreach (var p in Process.GetProcessesByName("explorer"))
                using (p)
                    try
                    {
                        if (p.SessionId != session) continue;
                        var t = new DateTimeOffset(p.StartTime.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeSeconds();
                        if (best is null || t < best) best = t;
                    }
                    catch { }
            return best;
        }
        catch { return null; }
    }
}
