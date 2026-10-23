using System.Diagnostics;
using System.IO;

namespace TimeSink.UI;

/// <summary>Cheap in-memory startup marks, flushed to ui.log together with the FlyoutShown line.</summary>
internal static class StartupTrace
{
    private static readonly List<(string Name, DateTime At, int Jit)> Marks = new();

    public static void Mark(string name) => Marks.Add((name, DateTime.Now, (int)System.Runtime.JitInfo.GetCompilationTime().TotalMilliseconds));

    public static void FlushShown() => Flush("FlyoutShown", "");

    public static void FlushStandbyReady() => Flush("StandbyReady", "");

    /// <summary>Warm path: <paramref name="ms"/> is the time from the Show signal to a placed, idle window.</summary>
    public static void FlushRevealed(double ms) => Flush("FlyoutShown", $" warm=1 signalMs={ms:0}");

    private static void Flush(string kind, string extra)
    {
        double since;
        using (var self = Process.GetCurrentProcess()) since = (DateTime.Now - self.StartTime).TotalMilliseconds;
        var start = DateTime.Now - TimeSpan.FromMilliseconds(since);
        var detail = string.Join(' ', Marks.Select(m => $"{m.Name}={(m.At - start).TotalMilliseconds:0}/j{m.Jit}"));
        Log($"{kind} ms={since:0}{extra} [{detail}]");
    }

    private static void Log(string line)
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeSink", "logs");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "ui.log"), $"{DateTime.Now:O} {line}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
