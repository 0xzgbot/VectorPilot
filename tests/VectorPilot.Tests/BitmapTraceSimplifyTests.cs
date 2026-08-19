using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// E1: marching-squares tracing emits a vertex per cell edge, which is far more
/// points than a toolpath needs. Douglas-Peucker simplification must cut the
/// count without moving the outline off the shape.
/// </summary>
public class BitmapTraceSimplifyTests
{
    /// <summary>A filled disc on a white background.</summary>
    private static (byte[] Pixels, int W, int H) Disc(int size = 64, double radius = 24)
    {
        var px = new byte[size * size];
        double c = size / 2.0;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double d = Math.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                px[y * size + x] = (byte)(d <= radius ? 255 : 0);
            }
        return (px, size, size);
    }

    [Fact]
    public void Simplification_Reduces_The_Point_Count()
    {
        var (px, w, h) = Disc();

        var raw = BitmapTracer.Trace(px, w, h, 128, simplifyTolerance: 0);
        var simplified = BitmapTracer.Trace(px, w, h, 128, simplifyTolerance: 1.0);

        Assert.NotEmpty(raw);
        Assert.NotEmpty(simplified);
        Assert.True(simplified[0].Points.Count < raw[0].Points.Count,
            $"expected fewer points: raw {raw[0].Points.Count} vs simplified {simplified[0].Points.Count}");
    }

    [Fact]
    public void Simplified_Outline_Still_Follows_The_Circle()
    {
        var (px, w, h) = Disc(64, 24);
        var simplified = BitmapTracer.Trace(px, w, h, 128, simplifyTolerance: 1.0);

        Assert.NotEmpty(simplified);
        // Every retained vertex must still sit near the true radius.
        foreach (var p in simplified[0].Points)
        {
            double d = Math.Sqrt((p.X - 32) * (p.X - 32) + (p.Y - 32) * (p.Y - 32));
            Assert.InRange(d, 22.0, 26.5);
        }
    }

    [Fact]
    public void Larger_Tolerance_Removes_More_Points()
    {
        var (px, w, h) = Disc();

        int tight = BitmapTracer.Trace(px, w, h, 128, 0.5)[0].Points.Count;
        int loose = BitmapTracer.Trace(px, w, h, 128, 3.0)[0].Points.Count;

        Assert.True(loose <= tight, $"looser tolerance kept more points: {loose} vs {tight}");
    }

    [Fact]
    public void Ring_Stays_Closed_After_Simplification()
    {
        var (px, w, h) = Disc();
        foreach (var shape in BitmapTracer.Trace(px, w, h, 128, 1.5))
        {
            Assert.True(shape.Closed);
            Assert.True(shape.Points.Count >= 3, "a ring must keep at least a triangle");
            Assert.True(shape.Points[0].DistanceTo(shape.Points[^1]) < 1e-6,
                "first and last point must coincide");
        }
    }

    [Fact]
    public void A_Square_Simplifies_Toward_Its_Corners()
    {
        // A 40x40 filled square in a 64x64 field: the outline is 4 straight runs,
        // so aggressive simplification should approach ~4-8 vertices.
        int size = 64;
        var px = new byte[size * size];
        for (int y = 12; y < 52; y++)
            for (int x = 12; x < 52; x++)
                px[y * size + x] = 255;

        var simplified = BitmapTracer.Trace(px, size, size, 128, simplifyTolerance: 1.0);
        Assert.NotEmpty(simplified);
        Assert.True(simplified[0].Points.Count <= 10,
            $"a square should reduce to a handful of vertices, got {simplified[0].Points.Count}");
    }

    [Fact]
    public void Zero_Tolerance_Preserves_Every_Point()
    {
        var (px, w, h) = Disc();
        var a = BitmapTracer.Trace(px, w, h, 128);                       // default overload
        var b = BitmapTracer.Trace(px, w, h, 128, simplifyTolerance: 0);  // explicit
        Assert.Equal(a[0].Points.Count, b[0].Points.Count);
    }
}
