using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class ToolpathSimulatorTests
{
    private static Heightmap Stock(int w = 10, int h = 10, double cell = 1.0, double top = 5.0)
        => new(w, h, cell, 0, 0, top);

    [Fact]
    public void Simulate_Carves_Trench_Along_G1()
    {
        var sim = new ToolpathSimulator(Stock());
        var gcode = new List<string>
        {
            "%", "O=TEST",
            "G0 X0 Y5",
            "G1 Z2 F100",          // plunge to Z=2 (below stock top 5)
            "G1 X9 Y5 F100"        // cut across
        };
        var r = sim.Simulate(gcode);
        Assert.True(r.Success);
        Assert.False(r.IsCancelled);
        // Cells along the row y=5, x=0..9 were lowered to Z=2.
        for (int x = 0; x < 10; x++)
        {
            Assert.Equal(2.0, r.FinalHeightmap.GetHeight(x, 5), 6);
        }
        // Cells far from the row remain at stock top.
        Assert.Equal(5.0, r.FinalHeightmap.GetHeight(5, 0), 6);
    }

    [Fact]
    public void Rapids_Do_Not_Remove_Material()
    {
        var sim = new ToolpathSimulator(Stock());
        var gcode = new List<string> { "G0 X5 Y5", "G0 Z1", "G0 X9 Y9" };
        var r = sim.Simulate(gcode);
        Assert.All(Enumerable.Range(0, 100), i => Assert.Equal(5.0, r.FinalHeightmap.Data[i], 6));
    }

    [Fact]
    public void Cancellation_Returns_Partial_Heightmap()
    {
        var sim = new ToolpathSimulator(Stock());
        var gcode = new List<string>
        {
            "G0 X0 Y5", "G1 Z2 F100", "G1 X9 Y5 F100",
            "G0 X0 Y3", "G1 Z2 F100", "G1 X9 Y3 F100"
        };
        int count = 0;
        var r = sim.Simulate(gcode, () => ++count > 4);
        Assert.True(r.IsCancelled);
    }

    [Fact]
    public void DraftHeightSamples_Produce_Grid()
    {
        var gcode = new List<string> { "G0 X0 Y0", "G1 Z1 F100", "G1 X50 Y50 F100" };
        var (samples, _) = ToolpathSimulator.DraftHeightSamples(gcode, cellSizeMm: 2.0, stockMm: 40);
        Assert.True(samples.Count > 50);
    }

    [Fact]
    public void ParseXY_Handles_Compact_And_Separated_Forms()
    {
        Assert.NotNull(WireframeRenderer.ParseXY("G0 X10 Y20", null, null));
        Assert.NotNull(WireframeRenderer.ParseXY("G0X10Y20", null, null));
        Assert.Null(WireframeRenderer.ParseXY("M3 S12000", null, null));
        Assert.Null(WireframeRenderer.ParseXY("G2 X10 Y10 I1 J1", null, null)); // arcs rejected
        var m = WireframeRenderer.ParseXY("G1 X5.5 Y7.25", 1, 2);
        Assert.NotNull(m);
        Assert.False(m!.IsRapid);
        Assert.Equal(5.5, m.X);
        Assert.Equal(7.25, m.Y);
    }

    [Fact]
    public void GenerateSegments_Produces_Rapid_And_Cut()
    {
        var gcode = new List<string>
        {
            "G0 X0 Y0", "G0 X10 Y0", "G1 Z1", "G1 X10 Y10"
        };
        var segs = WireframeRenderer.GenerateSegments(gcode);
        Assert.Equal(3, segs.Count);
        Assert.True(segs[0].IsRapid);
        Assert.False(segs[1].IsRapid); // Z-only line doesn't create a segment
        Assert.False(segs[2].IsRapid);
        Assert.Equal(10, segs[2].End.X, 6);
        Assert.Equal(10, segs[2].End.Y, 6);
    }
}
