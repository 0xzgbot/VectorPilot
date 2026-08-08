using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class BooleanOpsTests
{
    private static List<VectorPoint> Rect(double x, double y, double w, double h) => new()
    {
        new VectorPoint(x, y), new VectorPoint(x + w, y),
        new VectorPoint(x + w, y + h), new VectorPoint(x, y + h)
    };

    private static double Area(List<VectorPoint> pts)
    {
        double sum = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return Math.Abs(sum / 2.0);
    }

    [Fact]
    public void Union_Of_Overlapping_Squares_Has_Expected_Area()
    {
        // A: 0..2 x 0..2, B: 1..3 x 1..3 → overlap 1x1, union area 7
        var result = BooleanOps.Union(Rect(0, 0, 2, 2), Rect(1, 1, 2, 2));
        double area = result.Sum(Area);
        Assert.Equal(7.0, area, 1);
        Assert.Single(result);
    }

    [Fact]
    public void Intersect_Of_Overlapping_Squares_Is_The_Overlap()
    {
        var result = BooleanOps.Intersect(Rect(0, 0, 2, 2), Rect(1, 1, 2, 2));
        Assert.Single(result);
        Assert.Equal(1.0, Area(result[0]), 1);
    }

    [Fact]
    public void Subtract_Leaves_L_Shape()
    {
        var result = BooleanOps.Subtract(Rect(0, 0, 2, 2), Rect(1, 1, 2, 2));
        Assert.Single(result);
        Assert.Equal(3.0, Area(result[0]), 1);
    }

    [Fact]
    public void Disjoint_Squares_Union_Returns_Two()
    {
        var result = BooleanOps.Union(Rect(0, 0, 1, 1), Rect(5, 5, 1, 1));
        Assert.Equal(2, result.Count);
        Assert.Equal(2.0, result.Sum(Area), 1);
    }

    [Fact]
    public void Disjoint_Squares_Intersect_Is_Empty()
    {
        Assert.Empty(BooleanOps.Intersect(Rect(0, 0, 1, 1), Rect(5, 5, 1, 1)));
    }

    [Fact]
    public void Contained_Square_Intersect_Is_Inner()
    {
        var result = BooleanOps.Intersect(Rect(0, 0, 4, 4), Rect(1, 1, 2, 2));
        Assert.Single(result);
        Assert.Equal(4.0, Area(result[0]), 1);
    }

    [Fact]
    public void Contained_Square_Union_Is_Outer()
    {
        var result = BooleanOps.Union(Rect(0, 0, 4, 4), Rect(1, 1, 2, 2));
        Assert.Single(result);
        Assert.Equal(16.0, Area(result[0]), 1);
    }
}
