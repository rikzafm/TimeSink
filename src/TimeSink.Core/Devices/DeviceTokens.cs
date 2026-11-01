namespace TimeSink.Core.Devices;

public enum DeviceShape
{
    Circle, // Device A (local platform)
    Square  // Device B (remote platform)
}

public static class DeviceTokens
{
    public const string BrushDeviceA = "Brush.DeviceA";
    public const string BrushDeviceB = "Brush.DeviceB";

    /// <summary>
    /// Device marker shape rule: circle for local device (platform A), square for remote (platform B).
    /// </summary>
    public static DeviceShape ShapeFor(bool isLocal) => isLocal ? DeviceShape.Circle : DeviceShape.Square;

    public static string BrushKeyFor(bool isLocal) => isLocal ? BrushDeviceA : BrushDeviceB;
}

