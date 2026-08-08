using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class SvgImporterTests
{
    private const string RectCirclePathSvg = """
        <svg width="200" height="200" xmlns="http://www.w3.org/2000/svg">
          <rect x="5" y="5" width="20" height="30" />
          <circle cx="50" cy="50" r="12" />
          <path d="M 10 10 L 20 10 L 20 20 Z" />
        </svg>
        """;

    [Fact]
    public void RectCirclePath_ParseToThreeShapes()
    {
        var shapes = SvgImporter.Parse(RectCirclePathSvg);

        Assert.Equal(3, shapes.Count);

        // Swift ordering: path elements first, then primitives.
        var path = shapes[0];
        Assert.Equal(ShapeType.Polyline, path.Type);
        Assert.True(path.Closed);
        // M L L Z -> (10,10), (20,10), (20,20); closed, duplicate close point removed.
        Assert.Equal(3, path.Points.Count);
        Assert.Equal(new VectorPoint(10, 10), path.Points[0]);
        Assert.Equal(new VectorPoint(20, 20), path.Points[^1]);

        var rect = shapes[1];
        Assert.Equal(ShapeType.Rectangle, rect.Type);
        var rectBounds = rect.Bounds();
        Assert.Equal(5, rectBounds.MinX);
        Assert.Equal(5, rectBounds.MinY);
        Assert.Equal(25, rectBounds.MaxX);
        Assert.Equal(35, rectBounds.MaxY);

        var circle = shapes[2];
        Assert.Equal(ShapeType.Circle, circle.Type);
        Assert.Equal(50, circle.Points[0].X);
        Assert.Equal(50, circle.Points[0].Y);
        Assert.Equal(12, circle.Radius);
    }

    [Fact]
    public void Path_WithHVCommands_ProducesClosedRectangle()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg">
              <path d="M 0 0 H 10 V 10 H 0 Z" />
            </svg>
            """;

        var shapes = SvgImporter.Parse(svg);

        Assert.Single(shapes);
        // (0,0)->(10,0)->(10,10)->(0,10)->close: right-angle corners -> Rectangle.
        var shape = shapes[0];
        Assert.Equal(ShapeType.Rectangle, shape.Type);
        var b = shape.Bounds();
        Assert.Equal(0, b.MinX);
        Assert.Equal(0, b.MinY);
        Assert.Equal(10, b.MaxX);
        Assert.Equal(10, b.MaxY);
    }

    [Fact]
    public void ViewBox_Scaling_DoublesCoordinates()
    {
        const string svg = """
            <svg width="200" height="200" viewBox="0 0 100 100" xmlns="http://www.w3.org/2000/svg">
              <rect x="10" y="10" width="10" height="10" />
              <circle cx="25" cy="25" r="5" />
            </svg>
            """;

        var shapes = SvgImporter.Parse(svg);

        Assert.Equal(2, shapes.Count);

        // 200/100 = 2x scale: rect at 10,10 size 10x10 lands at 20,20 size 20x20.
        var rect = shapes[0];
        var b = rect.Bounds();
        Assert.Equal(20, b.MinX);
        Assert.Equal(20, b.MinY);
        Assert.Equal(40, b.MaxX);
        Assert.Equal(40, b.MaxY);

        // Circle center (25,25) -> (50,50), radius 5 -> 10.
        var circle = shapes[1];
        Assert.Equal(50, circle.Points[0].X);
        Assert.Equal(50, circle.Points[0].Y);
        Assert.Equal(10, circle.Radius);
    }

    [Fact]
    public void LineAndPolylineAndPolygon_Parse()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg">
              <line x1="0" y1="0" x2="10" y2="20" />
              <polyline points="0,0 10,0 10,10" />
              <polygon points="0,0 10,0 5,10" />
            </svg>
            """;

        var shapes = SvgImporter.Parse(svg);

        Assert.Equal(3, shapes.Count);

        var line = shapes[0];
        Assert.Equal(ShapeType.Line, line.Type);
        Assert.Equal(new VectorPoint(0, 0), line.Points[0]);
        Assert.Equal(new VectorPoint(10, 20), line.Points[1]);

        var polyline = shapes[1];
        Assert.Equal(ShapeType.Polyline, polyline.Type);
        Assert.False(polyline.Closed);
        Assert.Equal(3, polyline.Points.Count);

        var polygon = shapes[2];
        Assert.Equal(ShapeType.Polyline, polygon.Type);
        Assert.True(polygon.Closed);
        Assert.Equal(4, polygon.Points.Count); // close point appended
        Assert.Equal(polygon.Points[0], polygon.Points[^1]);
    }

    [Fact]
    public void ViewBox_Translation_OffsetsCoordinates()
    {
        const string svg = """
            <svg width="100" height="100" viewBox="10 20 100 100" xmlns="http://www.w3.org/2000/svg">
              <rect x="10" y="20" width="10" height="10" />
            </svg>
            """;

        var shapes = SvgImporter.Parse(svg);

        Assert.Single(shapes);
        // scale = 100/100 = 1; offset = -minX*scale = -10, -20.
        // Rect at (10,20) lands at (0,0), size 10x10.
        var b = shapes[0].Bounds();
        Assert.Equal(0, b.MinX, 6);
        Assert.Equal(0, b.MinY, 6);
        Assert.Equal(10, b.MaxX, 6);
        Assert.Equal(10, b.MaxY, 6);
    }

    [Fact]
    public void TextAndImageElements_AreSkipped()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg">
              <text x="0" y="0">Hello</text>
              <image href="foo.png" x="0" y="0" width="10" height="10" />
              <circle cx="1" cy="2" r="3" />
            </svg>
            """;

        var shapes = SvgImporter.Parse(svg);

        Assert.Single(shapes);
        Assert.Equal(ShapeType.Circle, shapes[0].Type);
    }

    [Fact]
    public void GarbageInput_DoesNotThrow()
    {
        Assert.Empty(SvgImporter.Parse(""));
        Assert.Empty(SvgImporter.Parse("not svg at all"));
        Assert.Empty(SvgImporter.Parse("<svg></svg>"));
    }
}
