using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// P4: material removal simulation. Sweeps posted G-code through a stock grid so
/// gouges, over-deep cuts and missed regions are visible before cutting.
/// </summary>
public class MaterialSimulationTests
{
    private static MaterialSimulator.Result Run(params string[] gcode)
        => MaterialSimulator.Simulate(gcode, 100, 100, 19.05, toolDiameter: 6, cellSizeMm: 1.0);

    [Fact]
    public void Untouched_Stock_Loses_Nothing()
    {
        var r = Run("G0 X0 Y0", "G0 X50 Y50");   // rapids above the surface
        Assert.Equal(0, r.RemovedVolumeMm3, 6);
        Assert.Equal(0, r.CoverageFraction, 6);
        Assert.False(r.CutThrough);
    }

    [Fact]
    public void A_Cutting_Pass_Removes_Material()
    {
        var r = Run("G0 X10 Y50", "G1 Z-3 F300", "G1 X90 Y50 F1000");
        Assert.True(r.RemovedVolumeMm3 > 0, "a cut must remove volume");
        Assert.True(r.CoverageFraction > 0);
        Assert.Equal(3.0, r.MaxCutDepthMm, 3);
    }

    [Fact]
    public void Deeper_Cuts_Remove_More()
    {
        var shallow = Run("G0 X10 Y50", "G1 Z-1 F300", "G1 X90 Y50 F1000");
        var deep = Run("G0 X10 Y50", "G1 Z-6 F300", "G1 X90 Y50 F1000");
        Assert.True(deep.RemovedVolumeMm3 > shallow.RemovedVolumeMm3);
    }

    [Fact]
    public void A_Wider_Tool_Removes_More()
    {
        string[] prog = { "G0 X10 Y50", "G1 Z-3 F300", "G1 X90 Y50 F1000" };
        var narrow = MaterialSimulator.Simulate(prog, 100, 100, 19.05, toolDiameter: 3);
        var wide = MaterialSimulator.Simulate(prog, 100, 100, 19.05, toolDiameter: 12);
        Assert.True(wide.RemovedVolumeMm3 > narrow.RemovedVolumeMm3);
    }

    [Fact]
    public void Cut_Through_Is_Flagged()
    {
        // 19.05mm stock, cutting to -25mm.
        var r = Run("G0 X50 Y50", "G1 Z-25 F300", "G1 X60 Y50 F1000");
        Assert.True(r.CutThrough, "cutting past the bottom face must be flagged");
    }

    [Fact]
    public void Staying_Above_The_Surface_Is_Not_Cut_Through()
    {
        var r = Run("G0 X50 Y50", "G1 Z-5 F300", "G1 X60 Y50 F1000");
        Assert.False(r.CutThrough);
        Assert.Equal(5.0, r.MaxCutDepthMm, 3);
    }

    [Fact]
    public void Full_Face_Clearing_Approaches_Complete_Coverage()
    {
        // Raster the whole 100x100 face at 5mm spacing with a 6mm tool.
        var prog = new List<string> { "G0 X0 Y0", "G1 Z-1 F300" };
        for (int y = 0; y <= 100; y += 5)
        {
            prog.Add($"G1 X0 Y{y} F1000");
            prog.Add($"G1 X100 Y{y} F1000");
        }

        var r = MaterialSimulator.Simulate(prog, 100, 100, 19.05, toolDiameter: 6, cellSizeMm: 1.0);
        Assert.True(r.CoverageFraction > 0.9, $"expected >90% coverage, got {r.CoverageFraction:P0}");
    }

    [Fact]
    public void Rapids_At_Safe_Z_Do_Not_Cut()
    {
        var cutting = Run("G0 X10 Y50", "G1 Z-3 F300", "G1 X90 Y50 F1000");
        var withRapids = Run(
            "G0 X10 Y50", "G1 Z-3 F300", "G1 X90 Y50 F1000",
            "G0 Z5", "G0 X10 Y20", "G0 X90 Y20");   // travel above the stock

        Assert.Equal(cutting.RemovedVolumeMm3, withRapids.RemovedVolumeMm3, 3);
    }

    [Fact]
    public void Comments_And_Non_Motion_Lines_Are_Ignored()
    {
        var r = Run("(VectorPilot)", "G90", "M3 S12000", "G0 X10 Y50",
                    "G1 Z-3 F300", "G1 X90 Y50 F1000", "M5", "M30");
        Assert.True(r.RemovedVolumeMm3 > 0);
        Assert.Equal(3.0, r.MaxCutDepthMm, 3);
    }

    [Fact]
    public void Result_Grid_Matches_The_Requested_Resolution()
    {
        var r = MaterialSimulator.Simulate(
            new[] { "G0 X0 Y0" }, 50, 80, 19.05, cellSizeMm: 2.0);
        Assert.Equal(25, r.Stock.Width);
        Assert.Equal(40, r.Stock.Height);
    }

    [Fact]
    public void Cut_Cells_Are_Negative_And_Uncut_Cells_Are_Zero()
    {
        var r = Run("G0 X50 Y50", "G1 Z-4 F300", "G1 X55 Y50 F1000");

        Assert.True(r.Stock.MinHeight <= -4 + 1e-6, $"deepest cell should reach -4, got {r.Stock.MinHeight}");
        Assert.Equal(0, r.Stock.MaxHeight, 6);   // untouched stock stays at the top surface
    }
}
