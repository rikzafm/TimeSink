namespace TimeSink.Core.Theming;

/// <summary>
/// List density (story 40, setting <c>ui.compact</c>): the row metrics the list views read from theme resources
/// (<c>Double.Row.*</c>, <c>Thickness.Row*</c>), so flipping the setting re-lays every list out at once. The regular
/// values are what the approved boards show.
/// </summary>
public sealed record Density(
    double Tile,            // app icon tile on a Today row
    double TileSmall,       // app tile on a Week row
    double TileLarge,       // icon tile in the Add app lists
    double Button,          // Track / Stop / Restore pills
    double TableRow,        // All table: tracked row
    double TableRetiredRow, // All table: retired row
    double TableHead,       // All table: header buttons
    double TableTotals,     // All table: totals row
    double CardPadY,        // Today row card padding, top and bottom
    double ListGapY,        // Week row margin, top and bottom
    double AddTileGapY,     // Add app (Running) tile margin, top and bottom
    double AddTileGapYTight)// Add app (Installed) tile margin, top and bottom
{
    public static readonly Density Regular = new(34, 28, 36, 32, 40, 34, 34, 38, 9, 6, 8, 6);
    public static readonly Density Compact = new(28, 24, 30, 28, 30, 26, 28, 30, 5, 3, 4, 3);

    public static Density For(bool compact) => compact ? Compact : Regular;
}
