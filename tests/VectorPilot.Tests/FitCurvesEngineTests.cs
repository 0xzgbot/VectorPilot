using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class FitCurvesEngineTests
{
    [Fact]
    public void Smoothing_Zero_Keeps_Points()
    {
        var pts = new List<VectorPoint> { new(0, 0), new(5, 5), new(10, 0) };
        var shape = VectorShape.Polyline(pts, closed: false);
        var r = FitCurvesEngine.Fit(shape, new FitCurvesParams { Smoothing = 0 });
        Assert.Equal(pts, r.Fitted);
        Assert.Equal(3, r.InputPointCount);
    }

    [Fact]
    public void Sharp_Corner_Is_Preserved()
    {
        // L-shape: the 90° turn at (5,5) must not move.
        var pts = new List<VectorPoint> { new(0, 0), new(5, 0), new(5, 5), new(5, 10) };
        var shape = VectorShape.Polyline(pts, closed: false);
        var r = FitCurvesEngine.Fit(shape, new FitCurvesParams { Smoothing = 1, CornerAngleDegrees = 30 });
        Assert.Equal(1, r.CornerCount);
        // The corner point stays exactly.
        Assert.Contains(r.Fitted, p => Math.Abs(p.X - 5) < 1e-9 && Math.Abs(p.Y - 5) < 1e-9);
    }

    [Fact]
    public void Straight_Line_Passes_Through_Bit_Exact()
    {
        var pts = new List<VectorPoint> { new(0, 0), new(3, 0), new(7, 0), new(10, 0) };
        var shape = VectorShape.Polyline(pts, closed: false);
        var r = FitCurvesEngine.Fit(shape, new FitCurvesParams { Smoothing = 1 });
        Assert.Equal(0, r.CornerCount);
        Assert.Equal(pts, r.Fitted); // collinear runs stay bit-exact
    }

    [Fact]
    public void Smoothing_Moves_Interior_Points_Toward_Average()
    {
        // Noisy line: interior points deviate; smoothing pulls them toward the mean.
        var pts = new List<VectorPoint> { new(0, 0), new(2, 8), new(4, 0), new(6, 8), new(8, 0) };
        var shape = VectorShape.Polyline(pts, closed: false);
        var r = FitCurvesEngine.Fit(shape, new FitCurvesParams { Smoothing = 1, CornerAngleDegrees = 170 });
        Assert.Equal(0, r.CornerCount);
        // Endpoints never move.
        Assert.Equal(pts[0], r.Fitted[0]);
        Assert.Equal(pts[^1], r.Fitted[^1]);
        // Interior points moved (their deviation shrank toward 0).
        Assert.True(Math.Abs(r.Fitted[1].Y) < 8);
    }

    [Fact]
    public void Resample_Subdivides_Long_Segments()
    {
        var pts = new List<VectorPoint> { new(0, 0), new(0, 100) };
        var shape = VectorShape.Polyline(pts, closed: false);
        var r = FitCurvesEngine.Fit(shape, new FitCurvesParams { MaxSegmentLengthMm = 25 });
        Assert.True(r.Fitted.Count >= 5);
    }

    [Fact]
    public void Degenerate_Input_Passes_Through()
    {
        var shape = VectorShape.Polyline(new List<VectorPoint> { new(0, 0), new(1, 1) }, closed: false);
        var r = FitCurvesEngine.Fit(shape, new FitCurvesParams { Smoothing = 1 });
        Assert.Equal(2, r.OutputPointCount);
        Assert.Equal(0, r.CornerCount);
    }

    [Fact]
    public void Circle_Is_Sampled_And_Smoothed()
    {
        var circle = VectorShape.Circle(new VectorPoint(0, 0), 10);
        var r = FitCurvesEngine.Fit(circle, new FitCurvesParams { Smoothing = 0.5 });
        Assert.True(r.InputPointCount >= 63);
        Assert.Equal(r.Fitted[0], r.Fitted[^1]); // closed with duplicate
    }
}
