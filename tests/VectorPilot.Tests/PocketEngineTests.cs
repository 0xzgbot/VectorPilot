using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class PocketEngineTests
{
    [Fact]
    public void Pocket_Of_Rectangle_Has_Two_Slices_At_Stepdown()
    {
        var rect = VectorShape.Rectangle(0, 0, 2, 1);
        var g = PocketEngine.Generate(new[] { rect }, cutDepth: 0.25, stepdown: 0.125,
            stepoverPercent: 40, feedRate: 100, plungeRate: 50, spindleSpeed: 12000, safeZ: 0.2);

        Assert.Contains(g, l => l.Contains("M3 S12000"));
        Assert.Contains(g, l => l.Contains("G20"));
        Assert.Contains(g, l => l.Contains("M5"));
        Assert.Contains(g, l => l.Contains("M30"));

        int slice1 = g.Count(l => l.StartsWith("G1 Z-0.1250"));
        int slice2 = g.Count(l => l.StartsWith("G1 Z-0.2500"));
        Assert.True(slice1 >= 3, $"expected >= 3 plunges in slice 1, got {slice1}");
        Assert.True(slice2 >= 3, $"expected >= 3 plunges in slice 2, got {slice2}");
    }

    [Fact]
    public void Pocket_Raster_Lines_Stay_Inside_Bounds()
    {
        var rect = VectorShape.Rectangle(0, 0, 2, 1);
        var g = PocketEngine.Generate(new[] { rect }, cutDepth: 0.1, stepdown: 0.1,
            stepoverPercent: 40, feedRate: 100, plungeRate: 50, spindleSpeed: 12000, safeZ: 0.2);

        foreach (var line in g.Where(l => l.StartsWith("G1 X")))
        {
            var parts = line.Split(' ');
            double x = double.Parse(parts[1][1..], System.Globalization.CultureInfo.InvariantCulture);
            double y = double.Parse(parts[2][1..], System.Globalization.CultureInfo.InvariantCulture);
            Assert.InRange(x, -0.01, 2.01);
            Assert.InRange(y, -0.01, 1.01);
        }
    }

    [Fact]
    public void Pocket_Empty_Shapes_Still_Emits_Header_Footer()
    {
        var g = PocketEngine.Generate(Array.Empty<VectorShape>(), cutDepth: 0.1, stepdown: 0.1,
            stepoverPercent: 40, feedRate: 100, plungeRate: 50, spindleSpeed: 12000, safeZ: 0.2);
        Assert.Contains(g, l => l.Contains("M3"));
        Assert.Contains(g, l => l.Contains("M30"));
    }
}
