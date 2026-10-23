using System.Globalization;

namespace TimeSink.Core.Settings;

/// <summary>Maths and wording for the About "Resource use" row (the UI samples the Agent process).</summary>
public static class ResourceUse
{
    public static double CpuPercent(TimeSpan cpuDelta, TimeSpan wallDelta, int cores)
        => wallDelta <= TimeSpan.Zero || cores <= 0 ? 0 : cpuDelta.TotalSeconds / (wallDelta.TotalSeconds * cores) * 100.0;

    public static string Format(long? workingSetBytes, double cpuPercent)
        => workingSetBytes is null
            ? "Agent not running"
            : string.Create(CultureInfo.InvariantCulture,
                $"{Math.Round(workingSetBytes.Value / 1048576.0):0} MB · {Math.Round(cpuPercent):0}% CPU");
}
