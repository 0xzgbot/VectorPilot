using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class DxfImporterTests
{
    private const string LineAndCircleDxf = """
        0
        SECTION
        2
        ENTITIES
        0
        LINE
        8
        Layer1
        10
        0.0
        20
        0.0
        11
        100.0
        21
        50.0
        0
        CIRCLE
        10
        25.0
        20
        25.0
        40
        10.0
        0
        ENDSEC
        0
        EOF
        """;

    [Fact]
    public void LineAndCircle_ParseToTwoShapes()
    {
        var shapes = DxfImporter.Parse(LineAndCircleDxf);

        Assert.Equal(2, shapes.Count);

        var line = shapes[0];
        Assert.Equal(ShapeType.Line, line.Type);
        Assert.Equal(0, line.Points[0].X);
        Assert.Equal(0, line.Points[0].Y);
        Assert.Equal(100, line.Points[1].X);
        Assert.Equal(50, line.Points[1].Y);

        var circle = shapes[1];
        Assert.Equal(ShapeType.Circle, circle.Type);
        Assert.Equal(25, circle.Points[0].X);
        Assert.Equal(25, circle.Points[0].Y);
        Assert.Equal(10, circle.Radius);
    }

    [Fact]
    public void Arc_ParsesToArcWithDegrees()
    {
        const string dxf = """
            0
            SECTION
            2
            ENTITIES
            0
            ARC
            10
            50.0
            20
            60.0
            40
            15.0
            50
            30.0
            51
            120.0
            0
            ENDSEC
            0
            EOF
            """;

        var shapes = DxfImporter.Parse(dxf);

        Assert.Single(shapes);
        var arc = shapes[0];
        Assert.Equal(ShapeType.Arc, arc.Type);
        Assert.Equal(50, arc.Points[0].X);
        Assert.Equal(60, arc.Points[0].Y);
        Assert.Equal(15, arc.Radius);
        Assert.Equal(30, arc.StartAngleDeg);
        Assert.Equal(120, arc.EndAngleDeg);
    }

    [Fact]
    public void LwPolyline_ClosedFlag_ClosesShape()
    {
        const string dxf = """
            0
            SECTION
            2
            ENTITIES
            0
            LWPOLYLINE
            70
            1
            10
            0.0
            20
            0.0
            10
            10.0
            20
            0.0
            10
            10.0
            20
            10.0
            0
            ENDSEC
            0
            EOF
            """;

        var shapes = DxfImporter.Parse(dxf);

        Assert.Single(shapes);
        var poly = shapes[0];
        Assert.Equal(ShapeType.Polyline, poly.Type);
        Assert.True(poly.Closed);
        // Closed: first point re-appended.
        Assert.Equal(4, poly.Points.Count);
        Assert.Equal(poly.Points[0], poly.Points[^1]);
        Assert.Equal(new VectorPoint(10, 10), poly.Points[^2]);
    }

    [Fact]
    public void PolylineWithVertices_Fallback_ProducesShape()
    {
        const string dxf = """
            0
            SECTION
            2
            ENTITIES
            0
            POLYLINE
            70
            1
            0
            VERTEX
            10
            0.0
            20
            0.0
            0
            VERTEX
            10
            5.0
            20
            5.0
            0
            SEQEND
            0
            ENDSEC
            0
            EOF
            """;

        var shapes = DxfImporter.Parse(dxf);

        Assert.Single(shapes);
        var poly = shapes[0];
        Assert.Equal(ShapeType.Polyline, poly.Type);
        Assert.True(poly.Closed);
        Assert.Equal(3, poly.Points.Count); // 2 vertices + re-appended first
        Assert.Equal(new VectorPoint(0, 0), poly.Points[0]);
        Assert.Equal(new VectorPoint(5, 5), poly.Points[1]);
    }

    [Fact]
    public void MalformedLine_IsSkipped_WithoutThrowing()
    {
        // LINE is missing the 21 (end Y) group code -> must be skipped, never fatal.
        const string dxf = """
            0
            SECTION
            2
            ENTITIES
            0
            LINE
            10
            0.0
            20
            0.0
            11
            100.0
            0
            CIRCLE
            10
            1.0
            20
            2.0
            40
            3.0
            0
            ENDSEC
            0
            EOF
            """;

        var shapes = DxfImporter.Parse(dxf);

        Assert.Single(shapes);
        Assert.Equal(ShapeType.Circle, shapes[0].Type);
    }

    [Fact]
    public void GarbageInput_DoesNotThrow()
    {
        Assert.Empty(DxfImporter.Parse(""));
        Assert.Empty(DxfImporter.Parse("this is not a dxf file at all\njust some random text"));
        Assert.Empty(DxfImporter.Parse("0\nSECTION\n2\nHEADER\n0\nENDSEC\n0\nEOF"));
    }
}
