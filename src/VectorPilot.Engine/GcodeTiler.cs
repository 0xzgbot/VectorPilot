using System.Globalization;

namespace VectorPilot.Engine;

/// <summary>
/// Splits a posted program into one G-code program per tile, for jobs larger than the
/// machine's envelope.
///
/// TilingEngine computed tile RECTANGLES but had zero App call-sites and no way to turn a
/// tile into a runnable program, so tiling was unreachable and did nothing.
/// </summary>
public static class GcodeTiler
{
    public sealed class Tile
    {
        public required TileRegion Region { get; init; }
        public List<string> Gcode { get; init; } = new();

        /// <summary>Cutting moves that fell inside this tile.</summary>
        public int CutMoveCount { get; init; }
    }

    public sealed class Result
    {
        public List<Tile> Tiles { get; init; } = new();
        public string? Error { get; init; }
        public bool Ok => Error is null;

        /// <summary>Tiles that contain at least one cutting move.</summary>
        public int NonEmptyTileCount => Tiles.Count(t => t.CutMoveCount > 0);
    }

    /// <summary>
    /// Split <paramref name="gcode"/> across tiles of the requested size. Each tile's
    /// program is shifted so the tile's own origin is at X0 Y0 — the machine cuts each
    /// tile with the stock re-zeroed, which is the whole point of tiling.
    /// </summary>
    public static Result Split(
        IReadOnlyList<string> gcode,
        double tileWidth, double tileHeight,
        double overlapMm = 5.0,
        bool shiftToTileOrigin = true)
    {
        if (gcode.Count == 0)
            return new Result { Error = "Nothing to tile — calculate a toolpath first." };

        if (tileWidth <= 0 || tileHeight <= 0)
            return new Result { Error = "Tile width and height must be greater than zero." };

        var moves = Parse(gcode);
        if (moves.Count == 0)
            return new Result { Error = "The program has no XY moves to tile." };

        double minX = moves.Min(m => m.X), maxX = moves.Max(m => m.X);
        double minY = moves.Min(m => m.Y), maxY = moves.Max(m => m.Y);

        var regions = TilingEngine.Tile(minX, minY, maxX, maxY, tileWidth, tileHeight, Math.Max(0, overlapMm));
        var tiles = new List<Tile>();

        foreach (var region in regions)
        {
            var lines = new List<string>
            {
                $"({region.Name} — X{region.MinX:F2}..{region.MaxX:F2} Y{region.MinY:F2}..{region.MaxY:F2})",
                "G90", "G21"
            };

            double dx = shiftToTileOrigin ? -region.MinX : 0;
            double dy = shiftToTileOrigin ? -region.MinY : 0;

            int cuts = 0;
            bool penDown = false;

            foreach (var m in moves)
            {
                bool inside = TilingEngine.Contains(region, m.X, m.Y);

                if (!inside)
                {
                    // Leaving the tile: lift so the next entry re-plunges rather than
                    // dragging the cutter across the tile boundary.
                    if (penDown) { lines.Add("G0 Z5.000"); penDown = false; }
                    continue;
                }

                string x = F(m.X + dx), y = F(m.Y + dy);

                if (m.IsCut)
                {
                    if (!penDown)
                    {
                        lines.Add($"G0 X{x} Y{y}");
                        if (m.Z is { } zEnter) lines.Add($"G1 Z{F(zEnter)}");
                        penDown = true;
                    }
                    lines.Add(m.Z is { } z
                        ? $"G1 X{x} Y{y} Z{F(z)}"
                        : $"G1 X{x} Y{y}");
                    cuts++;
                }
                else
                {
                    if (penDown) { lines.Add("G0 Z5.000"); penDown = false; }
                    lines.Add($"G0 X{x} Y{y}");
                }
            }

            if (penDown) lines.Add("G0 Z5.000");
            lines.Add("M5");

            tiles.Add(new Tile { Region = region, Gcode = lines, CutMoveCount = cuts });
        }

        return new Result { Tiles = tiles };
    }

    private static string F(double v) => v.ToString("F3", CultureInfo.InvariantCulture);

    private readonly record struct Move(double X, double Y, double? Z, bool IsCut);

    /// <summary>Track modal XYZ across the program, as a controller does.</summary>
    private static List<Move> Parse(IReadOnlyList<string> gcode)
    {
        var moves = new List<Move>();
        double x = 0, y = 0;
        double? z = null;

        foreach (var raw in gcode)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('(') || line.StartsWith('%')) continue;

            bool isG0 = line.StartsWith("G0", StringComparison.OrdinalIgnoreCase);
            bool isG1 = line.StartsWith("G1", StringComparison.OrdinalIgnoreCase);
            if (!isG0 && !isG1) continue;

            bool sawXy = false;
            foreach (var tok in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (tok.Length < 2) continue;
                if (!double.TryParse(tok[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;

                switch (char.ToUpperInvariant(tok[0]))
                {
                    case 'X': x = v; sawXy = true; break;
                    case 'Y': y = v; sawXy = true; break;
                    case 'Z': z = v; break;
                }
            }

            if (sawXy) moves.Add(new Move(x, y, z, isG1));
        }

        return moves;
    }
}
