using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class VectorOffsetTests
{
    private static List<VectorPoint> Square(double size, bool reversed = false)
    {
        var pts = new List<VectorPoint>
        {
            new(0, 0), new(size, 0), new(size, size), new(0, size)
        };
        if (reversed) pts.Reverse(); // clockwise
        return pts;
    }

    [Fact]
    public void OffsetClosedPolyline_Outward_Grows_Bounds_By_Distance()
    {
        var r = VectorOffset.OffsetClosedPolyline(Square(2), 0.5);
        Assert.NotNull(r);
        var b = VectorPilot.Geometry.BoundingBox.FromPoints(r!.OffsetPath);
        Assert.Equal(-0.5, b.MinX, 6);
        Assert.Equal(-0.5, b.MinY, 6);
        Assert.Equal(2.5, b.MaxX, 6);
        Assert.Equal(2.5, b.MaxY, 6);
    }

    [Fact]
    public void OffsetClosedPolyline_Inward_Shrinks_Bounds()
    {
        var r = VectorOffset.OffsetClosedPolyline(Square(2), -0.25);
        Assert.NotNull(r);
        var b = VectorPilot.Geometry.BoundingBox.FromPoints(r!.OffsetPath);
        Assert.Equal(0.25, b.MinX, 6);
        Assert.Equal(1.75, b.MaxX, 6);
    }

    [Fact]
    public void OffsetClosedPolyline_Is_Winding_Aware()
    {
        // Clockwise input must still expand OUTWARD for positive distance.
        var ccw = VectorOffset.OffsetClosedPolyline(Square(2), 0.5)!;
        var cw = VectorOffset.OffsetClosedPolyline(Square(2, reversed: true), 0.5)!;
        var bCcw = VectorPilot.Geometry.BoundingBox.FromPoints(ccw.OffsetPath);
        var bCw = VectorPilot.Geometry.BoundingBox.FromPoints(cw.OffsetPath);
        Assert.Equal(bCcw.MinX, bCw.MinX, 6);
        Assert.Equal(bCcw.MaxX, bCw.MaxX, 6);
        Assert.Equal(-0.5, bCw.MinX, 6);
    }

    [Fact]
    public void OffsetClosedPolyline_Returns_Null_On_Collapse()
    {
        var r = VectorOffset.OffsetClosedPolyline(Square(2), -1.5);
        Assert.Null(r); // inward offset larger than half the width collapses
    }

    [Fact]
    public void OffsetCircle_Adjusts_Radius()
    {
        var circle = VectorShape.Circle(new VectorPoint(0, 0), 1);
        var result = VectorOffset.OffsetShape(circle, 0.5);
        Assert.Single(result);
        Assert.Equal(ShapeType.Circle, result[0].Type);
        Assert.Equal(1.5, result[0].Radius, 6);
    }

    [Fact]
    public void OffsetCircle_Samples_Full_Circumference()
    {
        var circle = VectorShape.Circle(new VectorPoint(0, 0), 1);
        var r = VectorOffset.OffsetCircle(circle, 0.25);
        Assert.NotNull(r);
        // 64 samples, no degenerate single point
        Assert.True(r!.OffsetPath.Count >= 60, $"expected ~64 samples, got {r.OffsetPath.Count}");
        double firstAngle = Math.Atan2(r.OffsetPath[0].Y, r.OffsetPath[0].X);
        double lastAngle = Math.Atan2(r.OffsetPath[^1].Y, r.OffsetPath[^1].X);
        Assert.True(Math.Abs(lastAngle - firstAngle) < 0.2, "samples should cover the full circle");
    }

    [Fact]
    public void OffsetRectangle_Expands_Edges()
    {
        var rect = VectorShape.Rectangle(0, 0, 4, 2);
        var result = VectorOffset.OffsetShape(rect, 0.5);
        Assert.Single(result);
        var b = result[0].Bounds();
        Assert.Equal(-0.5, b.MinX, 6);
        Assert.Equal(-0.5, b.MinY, 6);
        Assert.Equal(4.5, b.MaxX, 6);
        Assert.Equal(2.5, b.MaxY, 6);
    }

    [Fact]
    public void OffsetLine_Shifts_To_Left_Normal()
    {
        var line = VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(2, 0));
        var r = VectorOffset.OffsetLine(line, 1);
        Assert.NotNull(r);
        Assert.Equal(0, r!.OffsetPath[0].X, 6);
        Assert.Equal(1, r.OffsetPath[0].Y, 6);
        Assert.Equal(2, r.OffsetPath[1].X, 6);
        Assert.Equal(1, r.OffsetPath[1].Y, 6);
    }

    [Fact]
    public void OffsetShape_Polyline_Returns_Closed_Offset()
    {
        var poly = VectorShape.Polyline(Square(2), closed: true);
        var result = VectorOffset.OffsetShape(poly, 0.25);
        Assert.Single(result);
        Assert.True(result[0].Closed);
        Assert.True(result[0].Points.Count >= 4);
    }
}
