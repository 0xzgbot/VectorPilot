namespace VectorPilot.Engine;

/// <summary>One tile of a tiled job (tiling).</summary>
public sealed class TileRegion
{
    public int Row { get; init; }
    public int Col { get; init; }
    public double MinX { get; init; }
    public double MinY { get; init; }
    public double MaxX { get; init; }
    public double MaxY { get; init; }
    public double Width => MaxX - MinX;
    public double Height => MaxY - MinY;
    public string Name => $"Tile R{Row + 1}C{Col + 1}";
}

/// <summary>
/// Splits a job bounding box into tiles with overlap (for machines smaller
/// than the part). Tiles cover the full region; adjacent tiles overlap by
/// OverlapMm so features straddling a seam are cut on both tiles.
/// </summary>
public static class TilingEngine
{
    public static List<TileRegion> Tile(double minX, double minY, double maxX, double maxY, double tileWidth, double tileHeight, double overlapMm = 5.0)
    {
        var tiles = new List<TileRegion>();
        if (tileWidth <= 0 || tileHeight <= 0 || maxX <= minX || maxY <= minY) return tiles;

        double stepX = Math.Max(0.1, tileWidth - overlapMm);
        double stepY = Math.Max(0.1, tileHeight - overlapMm);
        double w = maxX - minX, h = maxY - minY;

        int cols = Math.Max(1, (int)Math.Ceiling((w - overlapMm) / stepX + 1e-9));
        int rows = Math.Max(1, (int)Math.Ceiling((h - overlapMm) / stepY + 1e-9));

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                double x0 = minX + c * stepX;
                double y0 = minY + r * stepY;
                double x1 = Math.Min(maxX, x0 + tileWidth);
                double y1 = Math.Min(maxY, y0 + tileHeight);
                tiles.Add(new TileRegion { Row = r, Col = c, MinX = x0, MinY = y0, MaxX = x1, MaxY = y1 });
            }
        }
        return tiles;
    }

    /// <summary>True when a point falls inside a tile (with a small epsilon).</summary>
    public static bool Contains(TileRegion tile, double x, double y, double epsilon = 1e-6)
        => x >= tile.MinX - epsilon && x <= tile.MaxX + epsilon && y >= tile.MinY - epsilon && y <= tile.MaxY + epsilon;
}
