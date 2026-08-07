using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class GeometryTests
{
    [Fact]
    public void Rectangle_Bounds_Are_Exact()
    {
        var r = VectorShape.Rectangle(1, 2, 5, 3);
        var b = r.Bounds();
        Assert.Equal(1, b.MinX);
        Assert.Equal(2, b.MinY);
        Assert.Equal(6, b.MaxX);
        Assert.Equal(5, b.MaxY);
    }

    [Fact]
    public void Circle_Bounds_Account_For_Radius()
    {
        var c = VectorShape.Circle(new VectorPoint(10, 10), 4);
        var b = c.Bounds();
        Assert.Equal(6, b.MinX);
        Assert.Equal(14, b.MaxX);
        Assert.Equal(8, b.Height);
    }

    [Fact]
    public void Rotate_Quarter_Turn_Is_Exact()
    {
        var p = Transform2D.Rotate(new VectorPoint(1, 0), VectorPoint.Zero, 90);
        Assert.Equal(0, p.X, 6);
        Assert.Equal(1, p.Y, 6);
    }

    [Fact]
    public void SignedArea_Positive_For_Ccw_Square()
    {
        var pts = new List<VectorPoint>
        {
            new(0, 0), new(1, 0), new(1, 1), new(0, 1)
        };
        Assert.True(GeometryMath.SignedArea(pts) > 0);
    }

    [Fact]
    public void Distance_To_Segment_Is_Zero_On_Line()
    {
        Assert.Equal(0, GeometryMath.DistanceSqToSegment(new VectorPoint(0.5, 0), new VectorPoint(0, 0), new VectorPoint(1, 0)), 6);
        Assert.Equal(0.25, GeometryMath.DistanceSqToSegment(new VectorPoint(0.5, 0.5), new VectorPoint(0, 0), new VectorPoint(1, 0)), 6);
    }
}
