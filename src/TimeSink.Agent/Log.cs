namespace TimeSink.Agent;

/// <summary>Plain-text rolling log (1 MB x 3) in %LocalAppData%\TimeSink\logs. Never logs window titles or content.</summary>
internal static class Log
{
    private const long MaxBytes = 1_000_000;
    private static readonly string s_dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TimeSink", "logs");

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(s_dir);
            var file = Path.Combine(s_dir, "agent.log");
            if (File.Exists(file) && new FileInfo(file).Length > MaxBytes) Roll(file);
            File.AppendAllText(file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}{Environment.NewLine}");
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void Roll(string file)
    {
        if (File.Exists(file + ".1")) File.Move(file + ".1", file + ".2", true);
        File.Move(file, file + ".1", true);
    }
}
