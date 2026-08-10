using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class WrappedFlutingTests
{
    [Fact]
    public void Y_Wraps_To_A_Degrees()
    {
        // One full circumference = 360°. Y=25 on Ø50 → 25/(π·50)·360 ≈ 57.296.
        var r = WrappedFlutingToolpathEngine.Compute(
            new List<VectorPoint> { new(0, 0), new(10, 25) },
            new WrappedFlutingParams { WrapDiameterMm = 50, CutDepthMm = 2, PassDepthMm = 2 });
        Assert.Equal("O=WRAPPED_FLUTING", r.Marker);
        Assert.Contains(r.Gcode, l => l.Contains("A57.296"));
        Assert.DoesNotContain(r.Gcode, l => l.Contains(" Y"));
    }

    [Fact]
    public void X_Stays_Axial()
    {
        var r = WrappedFlutingToolpathEngine.Compute(
            new List<VectorPoint> { new(0, 0), new(42, 0) },
            new WrappedFlutingParams { WrapDiameterMm = 50 });
        Assert.Contains(r.Gcode, l => l.Contains("X42.000"));
        Assert.Contains(r.Gcode, l => l.Contains("A0.000"));
    }

    [Fact]
    public void CounterClockwise_Mirrors_Angle()
    {
        var r = WrappedFlutingToolpathEngine.Compute(
            new List<VectorPoint> { new(0, 0), new(10, 25) },
            new WrappedFlutingParams { WrapDiameterMm = 50, Direction = WrapDirection.CounterClockwise });
        Assert.Contains(r.Gcode, l => l.Contains("A302.704")); // 360 − 57.296
    }

    [Fact]
    public void Step_Down_Passes_And_Spindle()
    {
        var r = WrappedFlutingToolpathEngine.Compute(
            new List<VectorPoint> { new(0, 0), new(10, 0) },
            new WrappedFlutingParams { CutDepthMm = 4, PassDepthMm = 2, SpindleRpm = 12000 });
        Assert.Contains(r.Gcode, l => l == "M3 S12000");
        Assert.Contains(r.Gcode, l => l.Contains("pass 1/2"));
        Assert.Contains(r.Gcode, l => l.Contains("pass 2/2"));
        Assert.True(r.MoveCount >= 2);
        Assert.Contains(r.Gcode, l => l == "M30");
    }

    [Fact]
    public void FromMaterial_Scales_Feeds()
    {
        var p = WrappedFlutingParams.FromMaterial(new Material { MaxDepthOfCutMm = 6, MaxFeedRateMmPerMin = 2000 });
        Assert.Equal(12, p.CutDepthMm);
        Assert.Equal(6, p.PassDepthMm);
        Assert.Equal(1400, p.FeedRateMmPerMin);
        Assert.Equal(600, p.PlungeRateMmPerMin);
    }

    [Fact]
    public void Degenerate_Input_Emits_Only_End()
    {
        var r = WrappedFlutingToolpathEngine.Compute(new List<VectorPoint> { new(0, 0) }, new WrappedFlutingParams());
        Assert.Equal(0, r.MoveCount);
        Assert.Contains(r.Gcode, l => l == "M30");
    }
}
