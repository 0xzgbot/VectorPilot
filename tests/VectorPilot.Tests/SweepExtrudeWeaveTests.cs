using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class SweepExtrudeWeaveTests
{
    [Fact]
    public void TwoRailSweep_Volume_Is_Rail_Times_Profile_Area()
    {
        var id = Guid.NewGuid();
        var r = SweepExtrudeWeaveEngine.TwoRailSweep(id, new TwoRailSweepParams
        {
            Rail1Points = new List<VectorPoint> { new(0, 0), new(10, 0) },
            Rail2Points = new List<VectorPoint> { new(0, 5), new(10, 5) },
            Profile = new SweepProfileParams { Width = 4, Height = 2 }
        });
        Assert.True(r.Success);
        Assert.Equal(10 * (4 * 2), r.VolumeMm3, 6); // rail 10mm × area 8mm²
        Assert.Equal(10 * (4 + 2), r.SurfaceAreaMm2, 6);
    }

    [Fact]
    public void TwoRailSweep_Mismatched_Rails_Fail()
    {
        var r = SweepExtrudeWeaveEngine.TwoRailSweep(Guid.NewGuid(), new TwoRailSweepParams
        {
            Rail1Points = new List<VectorPoint> { new(0, 0), new(10, 0) },
            Rail2Points = new List<VectorPoint> { new(0, 5), new(5, 5), new(10, 5) }
        });
        Assert.False(r.Success);
        Assert.Contains("same number", r.ErrorMessage);
    }

    [Fact]
    public void Extrude_Bilateral_Doubles_Volume()
    {
        var one = SweepExtrudeWeaveEngine.Extrude(Guid.NewGuid(), new ExtrudeParams { Distance = 5 }, 10, 10);
        var both = SweepExtrudeWeaveEngine.Extrude(Guid.NewGuid(), new ExtrudeParams { Distance = 5, Bilateral = true }, 10, 10);
        Assert.Equal(500, one.VolumeMm3, 6);
        Assert.Equal(1000, both.VolumeMm3, 6);
    }

    [Fact]
    public void Extrude_Zero_Direction_Fails()
    {
        var r = SweepExtrudeWeaveEngine.Extrude(Guid.NewGuid(), new ExtrudeParams { Direction = new Vector3(0, 0, 0) }, 10, 10);
        Assert.False(r.Success);
    }

    [Fact]
    public void Weave_Volume_Scales_With_Threads()
    {
        var r = SweepExtrudeWeaveEngine.Weave(Guid.NewGuid(), new WeaveParams { ThreadSize = 1, WarpCount = 10, WeftCount = 10, Overlap = 0.5 }, 20, 10);
        Assert.True(r.Success);
        double threadLen = 20 * Math.Max(20, 10); // 20 threads × 20mm
        Assert.Equal(threadLen * 1.0 * 0.5, r.VolumeMm3, 6);
    }

    [Fact]
    public void Validation_Reports_Errors()
    {
        var (ok, errors) = SweepExtrudeWeaveEngine.ValidateTwoRailSweep(new TwoRailSweepParams());
        Assert.False(ok);
        Assert.Equal(2, errors.Count);

        var (ok2, _) = SweepExtrudeWeaveEngine.ValidateWeave(new WeaveParams { WarpCount = 0 });
        Assert.False(ok2);
    }
}
