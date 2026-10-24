using System.Diagnostics;

namespace TimeSink.UI.Services;

/// <summary>Best-effort sign-in time: the earliest <c>explorer.exe</c> start in this Windows session (null when unknown).</summary>
internal static class SignInTime
{
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
