using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class FilletExtendTests
{
    [Fact]
    public void Fillet_Rectangle_Produces_Rounded_Corners()
    {
        var rect = VectorShape.Rectangle(0, 0, 10, 10);
        var rounded = ShapeFilletEngine.Fillet(rect, 2.0);
        Assert.Equal(ShapeType.Polyline, rounded.Type);
        Assert.True(rounded.Points.Count > 4); // arcs added
        // Rounded corners stay inside the original bounds.
        var b = rounded.Bounds();
        Assert.True(b.MinX >= -1e-9 && b.MaxX <= 10 + 1e-9);
    }

    [Fact]
    public void Fillet_Square_Corner_Arc_Geometry()
    {
        // Fillet the 90° corner at (10,10) of an L: (0,0)→(10,0)→(10,10).
        var pts = new List<VectorPoint> { new(0, 0), new(10, 0), new(10, 10) };
        var filleted = ShapeFilletEngine.FilletPolyline(pts, 2.0);
        Assert.True(filleted.Count >= 5);
        // Tangent points: 2mm from the corner along each segment.
        Assert.Contains(filleted, p => Math.Abs(p.DistanceTo(new VectorPoint(8, 0))) < 1e-6);
        Assert.Contains(filleted, p => Math.Abs(p.DistanceTo(new VectorPoint(10, 2))) < 1e-6);
        // The sharp corner vertex is gone.
        Assert.DoesNotContain(filleted, p => Math.Abs(p.DistanceTo(new VectorPoint(10, 0))) < 1e-6 && p.Y < 1e-6);
    }

    [Fact]
    public void Fillet_Closed_Loop_Wraps()
    {
        var square = new List<VectorPoint>
        {
            new(0, 0), new(10, 0), new(10, 10), new(0, 10), new(0, 0)
        };
        var filleted = ShapeFilletEngine.FilletPolyline(square, 1.0);
        Assert.True(filleted.Count > 5);
        Assert.Equal(filleted[0], filleted[^1]); // loop convention kept
    }

    [Fact]
    public void Extend_Line_Both_Ends()
    {
        var line = VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(10, 0));
        var extended = ShapeExtendEngine.Extend(line, 2.0);
        Assert.Equal(-2.0, extended.Points[0].X, 6);
        Assert.Equal(12.0, extended.Points[1].X, 6);
    }

    [Fact]
    public void Extend_Open_Polyline_Ends_Only()
    {
        var poly = VectorShape.Polyline(new List<VectorPoint> { new(0, 0), new(10, 0), new(10, 10) }, closed: false);
        var extended = ShapeExtendEngine.Extend(poly, 1.0);
        Assert.Equal(-1.0, extended.Points[0].X, 6);
        Assert.Equal(10.0, extended.Points[^1].X, 6); // last segment is vertical; X unchanged
        Assert.Equal(11.0, extended.Points[^1].Y, 6);
    }

    [Fact]
    public void FilletExtend_Single_Corner_Nearest_Point()
    {
        var rect = VectorShape.Rectangle(0, 0, 10, 10);
        var segments = FilletExtendEngine.Fillet(rect, new VectorPoint(10, 10), 2.0);
        Assert.True(segments.Count >= 4);
        Assert.All(segments, s => Assert.Equal(ShapeType.Line, s.Type));
    }

    [Fact]
    public void ExtendLine_To_Projected_Point()
    {
        var line = VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(10, 0));
        var extended = FilletExtendEngine.ExtendLine(line, new VectorPoint(15, 3));
        Assert.Single(extended);
        Assert.Equal(15.0, extended[0].Points[1].X, 6);
        Assert.Equal(3.0, extended[0].Points[1].Y, 6);
    }
}
