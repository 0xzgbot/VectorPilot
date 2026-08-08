using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class VectorValidatorTests
{
    [Fact]
    public void Open_Loop_Detected()
    {
        var open = VectorShape.Polyline(new List<VectorPoint> { new(0, 0), new(10, 0), new(10, 10) }, closed: false);
        var issues = VectorValidator.Validate(new[] { open });
        Assert.Contains(issues, i => i.Message.Contains("Open loop"));
    }

    [Fact]
    public void Closed_Shape_Is_Clean()
    {
        var rect = VectorShape.Rectangle(0, 0, 10, 10);
        Assert.Empty(VectorValidator.Validate(new[] { rect }));
    }

    [Fact]
    public void Self_Intersection_Detected()
    {
        // Bow-tie: (0,0),(10,10),(0,10),(10,0) — crossing diagonals.
        var bowtie = VectorShape.Polyline(new List<VectorPoint>
        {
            new(0, 0), new(10, 10), new(0, 10), new(10, 0), new(0, 0)
        }, closed: true);
        Assert.True(VectorValidator.HasSelfIntersection(bowtie));
        var issues = VectorValidator.Validate(new[] { bowtie });
        Assert.Contains(issues, i => i.Message.Contains("self-intersect"));
    }

    [Fact]
    public void Duplicate_Points_Warn()
    {
        var dup = VectorShape.Polyline(new List<VectorPoint>
        {
            new(0, 0), new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0)
        }, closed: true);
        var issues = VectorValidator.Validate(new[] { dup });
        Assert.Contains(issues, i => i.Message.Contains("Duplicate"));
    }

    [Fact]
    public void Empty_And_Single_Point_Are_Errors()
    {
        var empty = VectorShape.Polyline(new List<VectorPoint>(), closed: false);
        var single = VectorShape.Polyline(new List<VectorPoint> { new(1, 1) }, closed: false);
        var issues = VectorValidator.Validate(new[] { empty, single });
        Assert.Contains(issues, i => i.Severity == VectorIssueSeverity.Error && i.Message.Contains("Empty"));
        Assert.Contains(issues, i => i.Severity == VectorIssueSeverity.Error && i.Message.Contains("Single-point"));
    }
}

public class ShapeFactoryTests
{
    [Fact]
    public void Arc_Quarter_Circle()
    {
        var pts = ShapeFactory.ArcPoints(new VectorPoint(0, 0), 10, 0, 90, 8);
        Assert.Equal(9, pts.Count);
        Assert.Equal(10.0, pts[0].X, 6);
        Assert.Equal(0.0, pts[0].Y, 6);
        Assert.Equal(0.0, pts[^1].X, 6);
        Assert.Equal(10.0, pts[^1].Y, 6);
    }

    [Fact]
    public void Regular_Polygon_Hexagon()
    {
        var pts = ShapeFactory.RegularPolygon(new VectorPoint(0, 0), 10, 6);
        Assert.Equal(6, pts.Count);
        Assert.All(pts, p => Assert.Equal(10.0, Math.Sqrt(p.X * p.X + p.Y * p.Y), 6));
    }

    [Fact]
    public void Star_Has_2N_Points()
    {
        var pts = ShapeFactory.Star(new VectorPoint(0, 0), 10, 4, 5);
        Assert.Equal(10, pts.Count);
    }

    [Fact]
    public void Spiral_Grows_Outward()
    {
        var pts = ShapeFactory.Spiral(new VectorPoint(0, 0), 1, 10, 2, 0, 64);
        Assert.True(pts.Count > 30);
        double r0 = Math.Sqrt(pts[0].X * pts[0].X + pts[0].Y * pts[0].Y);
        double r1 = Math.Sqrt(pts[^1].X * pts[^1].X + pts[^1].Y * pts[^1].Y);
        Assert.True(r1 > r0);
    }

    [Fact]
    public void Ellipse_Axis_Aligned()
    {
        var pts = ShapeFactory.EllipsePoints(new VectorPoint(0, 0), 5, 2);
        Assert.Equal(65, pts.Count);
        Assert.Equal(5.0, pts[0].X, 6);
        Assert.Equal(2.0, pts[16].Y, 6); // 90° up
    }
}
