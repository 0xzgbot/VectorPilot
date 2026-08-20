using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Tiling a posted program — the path OutputPanel.ExportTiles_Click uses.
///
/// TilingEngine computed tile RECTANGLES but had zero VectorPilot.App call-sites and no
/// way to turn a tile into a runnable program, so a job larger than the machine envelope
/// could not be cut at all.
/// </summary>
public class GcodeTilerTests
{
    /// <summary>A 1000x200 zig-zag: wider than one 400mm tile.</summary>
    private static List<string> WidePath()
    {
        var g = new List<string> { "%", "G90", "G21", "M3 S12000", "G0 Z5.000" };
        for (int x = 0; x <= 1000; x += 50)
        {
            g.Add($"G0 X{x}.000 Y0.000");
            g.Add($"G1 X{x}.000 Y150.000 Z-2.000");
        }
        g.Add("M5");
        return g;
    }

    private static List<string> SmallPath() => new()
    {
        "G90", "G21", "G0 X10.000 Y10.000", "G1 X60.000 Y10.000 Z-1.000",
        "G1 X60.000 Y60.000 Z-1.000", "M5"
    };

    // ---- splitting ----

    [Fact]
    public void A_Path_Larger_Than_One_Tile_Produces_At_Least_Two_Tiles()
    {
        var r = GcodeTiler.Split(WidePath(), tileWidth: 400, tileHeight: 400);

        Assert.True(r.Ok, r.Error);
        Assert.True(r.NonEmptyTileCount >= 2,
            $"1000mm path in 400mm tiles produced {r.NonEmptyTileCount} non-empty tile(s)");
    }

    [Fact]
    public void A_Path_Smaller_Than_One_Tile_Produces_One_Tile()
    {
        var r = GcodeTiler.Split(SmallPath(), tileWidth: 600, tileHeight: 400);

        Assert.True(r.Ok, r.Error);
        Assert.Equal(1, r.NonEmptyTileCount);
    }

    [Fact]
    public void Every_Cut_Move_Lands_In_Some_Tile()
    {
        var r = GcodeTiler.Split(WidePath(), tileWidth: 400, tileHeight: 400);

        Assert.True(r.Ok, r.Error);
        Assert.True(r.Tiles.Sum(t => t.CutMoveCount) > 0, "no cutting moves survived tiling");
    }

    [Fact]
    public void Overlap_Is_Non_Zero_When_Set()
    {
        var r = GcodeTiler.Split(WidePath(), tileWidth: 300, tileHeight: 300, overlapMm: 12);

        Assert.True(r.Ok, r.Error);

        // Neighbouring tiles in the same row must share a strip.
        var row0 = r.Tiles.Where(t => t.Region.Row == 0).OrderBy(t => t.Region.Col).ToList();
        Assert.True(row0.Count >= 2, "expected at least two columns to compare");

        double shared = row0[0].Region.MaxX - row0[1].Region.MinX;
        Assert.True(shared > 0, $"tiles do not overlap (shared strip {shared:F3}mm)");
    }

    [Fact]
    public void Zero_Overlap_Yields_Abutting_Tiles()
    {
        var r = GcodeTiler.Split(WidePath(), tileWidth: 300, tileHeight: 300, overlapMm: 0);

        Assert.True(r.Ok, r.Error);
        var row0 = r.Tiles.Where(t => t.Region.Row == 0).OrderBy(t => t.Region.Col).ToList();

        if (row0.Count >= 2)
        {
            double shared = row0[0].Region.MaxX - row0[1].Region.MinX;
            Assert.True(Math.Abs(shared) < 1e-6, $"expected abutting tiles, shared {shared:F3}mm");
        }
    }

    [Fact]
    public void More_Overlap_Means_More_Tiles_For_The_Same_Path()
    {
        int few = GcodeTiler.Split(WidePath(), 300, 300, overlapMm: 0).Tiles.Count;
        int many = GcodeTiler.Split(WidePath(), 300, 300, overlapMm: 100).Tiles.Count;

        Assert.True(many >= few,
            $"100mm overlap gave {many} tiles vs {few} with none — overlap is not reducing stride");
    }

    // ---- each tile is a runnable program ----

    [Fact]
    public void Each_Tile_Program_Is_Self_Contained()
    {
        var r = GcodeTiler.Split(WidePath(), 400, 400);

        foreach (var tile in r.Tiles.Where(t => t.CutMoveCount > 0))
        {
            Assert.Contains("G90", tile.Gcode);
            Assert.Contains("G21", tile.Gcode);
            Assert.Contains("M5", tile.Gcode);
            Assert.Contains(tile.Gcode, l => l.StartsWith("G1 X"));
        }
    }

    [Fact]
    public void Tile_Coordinates_Are_Shifted_To_The_Tile_Origin()
    {
        // The far-right tile must not emit X900 — the stock is re-zeroed per tile.
        var r = GcodeTiler.Split(WidePath(), 400, 400);
        var last = r.Tiles.Where(t => t.CutMoveCount > 0).OrderBy(t => t.Region.Col).Last();

        var xs = last.Gcode
            .Where(l => (l.StartsWith("G0 ") || l.StartsWith("G1 ")) && l.Contains(" X"))
            .Select(l => l.Split(' ').First(t => t.StartsWith('X'))[1..])
            .Select(s => double.Parse(s, System.Globalization.CultureInfo.InvariantCulture))
            .ToList();

        Assert.NotEmpty(xs);
        Assert.True(xs.Max() <= 400 + 1e-3,
            $"tile emits X{xs.Max():F2}, outside its own 400mm envelope");
        Assert.True(xs.Min() >= -1e-3, $"tile emits negative X{xs.Min():F2}");
    }

    [Fact]
    public void Unshifted_Mode_Keeps_Original_Coordinates()
    {
        var r = GcodeTiler.Split(WidePath(), 400, 400, shiftToTileOrigin: false);
        var last = r.Tiles.Where(t => t.CutMoveCount > 0).OrderBy(t => t.Region.Col).Last();

        Assert.Contains(last.Gcode, l => l.Contains("X9") || l.Contains("X8") || l.Contains("X7"));
    }

    [Fact]
    public void The_Cutter_Lifts_When_Leaving_A_Tile()
    {
        // Otherwise the tool would drag straight across the tile boundary.
        var r = GcodeTiler.Split(WidePath(), 200, 400);
        var tile = r.Tiles.First(t => t.CutMoveCount > 0);

        Assert.Contains(tile.Gcode, l => l.StartsWith("G0 Z"));
    }

    // ---- refusals ----

    [Fact]
    public void An_Empty_Program_Is_Refused()
    {
        var r = GcodeTiler.Split(Array.Empty<string>(), 400, 400);

        Assert.False(r.Ok);
        Assert.Contains("Nothing to tile", r.Error!);
    }

    [Fact]
    public void A_Zero_Tile_Size_Is_Refused()
    {
        Assert.False(GcodeTiler.Split(WidePath(), 0, 400).Ok);
        Assert.False(GcodeTiler.Split(WidePath(), 400, 0).Ok);
    }

    [Fact]
    public void A_Comment_Only_Program_Is_Refused()
    {
        var r = GcodeTiler.Split(new[] { "%", "(nothing here)", "M5" }, 400, 400);

        Assert.False(r.Ok);
        Assert.Contains("no XY moves", r.Error!);
    }
}
