using System.Globalization;

namespace TimeSink.Core.Sync;

/// <summary>Display strings for "when was the other device's file written", shared by Settings and the flyout.</summary>
public static class RemoteFileText
{
    public const long StaleAfterSeconds = 24 * 3600;

    public readonly record struct Result(string Line, string? StaleHint);

    /// <param name="lastFileUtc">Unix seconds of the newest remote file we read; 0 or less means never.</param>
    public static Result Describe(long lastFileUtc, DateTimeOffset now)
    {
        if (lastFileUtc <= 0) return new Result("File not read yet", null);
        var age = Math.Max(0, now.ToUnixTimeSeconds() - lastFileUtc);
        string line;
        if (age < 60) line = "File from just now";
        else if (age < 3600) line = $"File from {age / 60} min ago";
        else if (age < 86400) line = $"File from {age / 3600} h ago";
        else
        {
            var days = age / 86400;
            line = days == 1 ? "File from 1 day ago" : $"File from {days} days ago";
        }
        string? hint = null;
        if (age >= StaleAfterSeconds)
        {
            var when = DateTimeOffset.FromUnixTimeSeconds(lastFileUtc).ToLocalTime();
            hint = "Other device hasn't synced since " + when.ToString("MMM d", CultureInfo.InvariantCulture);
        }
        return new Result(line, hint);
    }
}
