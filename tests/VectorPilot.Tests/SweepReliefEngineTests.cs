using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class SweepReliefEngineTests
{
    private static List<VectorPoint> Line(double x0, double x1, double y)
        => new() { new VectorPoint(x0, y), new VectorPoint(x1, y) };

    [Fact]
    public void Rectangle_Profile_Sweeps_Flat_Strip()
    {
        // Rails at y=0 and y=10, x from 0 to 20 → 20x10 strip.
        var hf = SweepReliefEngine.Sweep(Line(0, 20, 0), Line(0, 20, 10), SweepProfile.Rectangle, height: 5, cellSizeMm: 1.0);
        Assert.NotNull(hf);
        Assert.True(hf!.Width >= 19);
        Assert.True(hf.Height >= 9);
        Assert.Equal(5.0, hf.MaxHeight, 6);
        // Center is inside the strip.
        var center = hf.HeightAt(hf.MinX + hf.Width / 2.0, hf.MinY + hf.Height / 2.0);
        Assert.Equal(5.0, center!.Value, 6);
    }

    [Fact]
    public void Circle_Profile_Domes_On_Centerline()
    {
        var hf = SweepReliefEngine.Sweep(Line(0, 20, 0), Line(0, 20, 10), SweepProfile.Circle, height: 8, cellSizeMm: 1.0);
        Assert.NotNull(hf);
        // Centerline height ≈ peak; edges near the rails ≈ 0.
        double cx = hf!.MinX + hf.Width / 2.0, cy = hf.MinY + hf.Height / 2.0;
        double center = hf.HeightAt(cx, cy)!.Value;
        Assert.True(center > 7.0, $"center {center}");
        double edge = hf.HeightAt(cx, hf.MinY + 0.5)!.Value;
        Assert.True(edge < 1.0, $"edge {edge}");
    }

    [Fact]
    public void Degenerate_Rails_Return_Null()
    {
        Assert.Null(SweepReliefEngine.Sweep(new List<VectorPoint>(), Line(0, 10, 5), SweepProfile.Rectangle, 1));
        Assert.Null(SweepReliefEngine.Sweep(new List<VectorPoint> { new(0, 0) }, Line(0, 10, 5), SweepProfile.Rectangle, 1));
    }

    [Fact]
    public void Resample_By_Length_Fraction()
    {
        var pts = new List<VectorPoint> { new(0, 0), new(10, 0), new(10, 10) }; // total 20
        var r = SweepReliefEngine.Resample(pts, 5);
        Assert.Equal(5, r.Count);
        Assert.Equal(0.0, r[0].X, 6);
        Assert.Equal(5.0, r[1].X, 6);  // 25% of 20 = 5
        Assert.Equal(10.0, r[2].X, 6); // 50% = 10, still on the first segment
        Assert.Equal(0.0, r[2].Y, 6);
        Assert.Equal(10.0, r[3].X, 6); // 75% = 15 → 5 up the second segment
        Assert.Equal(5.0, r[3].Y, 6);
        Assert.Equal(10.0, r[4].X, 6);
        Assert.Equal(10.0, r[4].Y, 6);
    }

    [Fact]
    public void PointInPolygon_RayCast()
    {
        var square = new List<VectorPoint>
        {
            new(0, 0), new(10, 0), new(10, 10), new(0, 10)
        };
        Assert.True(SweepReliefEngine.PointInPolygon(new VectorPoint(5, 5), square));
        Assert.False(SweepReliefEngine.PointInPolygon(new VectorPoint(15, 5), square));
    }
}
