using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class EpsImporterTests
{
    [Fact]
    public void MovetoLinetoClosepath_ProducesClosedPolyline()
    {
        const string eps = """
            %!PS-Adobe-3.0 EPSF-3.0
            %%BoundingBox: 0 0 100 100
            newpath
            0 0 moveto
            100 0 lineto
            100 100 lineto
            closepath
            stroke
            showpage
            """;

        var shapes = EpsImporter.Parse(eps);

        var poly = Assert.Single(shapes);
        Assert.Equal(ShapeType.Polyline, poly.Type);
        Assert.True(poly.Closed);
        Assert.True(poly.Points.Count >= 3);
        // closepath re-appends the subpath start so first == last.
        Assert.Equal(poly.Points[0], poly.Points[^1]);
        Assert.Equal(new VectorPoint(0, 0), poly.Points[0]);
        Assert.Equal(new VectorPoint(100, 0), poly.Points[1]);
        Assert.Equal(new VectorPoint(100, 100), poly.Points[2]);
    }

    [Fact]
    public void BoundingBox_OffsetsAllCoordinates()
    {
        const string eps = """
            %!PS-Adobe-3.0 EPSF-3.0
            %%BoundingBox: 10 20 110 120
            newpath
            10 20 moveto
            110 20 lineto
            110 120 lineto
            closepath
            stroke
            """;

        var shapes = EpsImporter.Parse(eps);

        var poly = Assert.Single(shapes);
        Assert.Equal(new VectorPoint(0, 0), poly.Points[0]);
        Assert.Equal(new VectorPoint(100, 0), poly.Points[1]);
        Assert.Equal(new VectorPoint(100, 100), poly.Points[2]);
    }

    [Fact]
    public void Curveto_IsSampledIntoPolylinePoints()
    {
        const string eps = """
            %!PS-Adobe-3.0 EPSF-3.0
            %%BoundingBox: 0 0 100 100
            newpath
            0 0 moveto
            50 100 100 100 100 0 curveto
            stroke
            """;

        var shapes = EpsImporter.Parse(eps);

        var poly = Assert.Single(shapes);
        Assert.Equal(ShapeType.Polyline, poly.Type);
        // 1 start point + 16 sampled segments = 17 points.
        Assert.Equal(17, poly.Points.Count);
        Assert.Equal(new VectorPoint(0, 0), poly.Points[0]);
        Assert.Equal(new VectorPoint(100, 0), poly.Points[^1]);
    }

    [Fact]
    public void TextAndRasterOperands_DoNotCorruptPathState()
    {
        const string eps = """
            %!PS-Adobe-3.0 EPSF-3.0
            %%BoundingBox: 0 0 100 100
            newpath
            0 0 moveto
            (Hello world) show
            100 100 lineto
            1 0 0 setrgbcolor
            10 10 20 20 30 30 setlinewidth
            closepath
            stroke
            """;

        var shapes = EpsImporter.Parse(eps);

        var poly = Assert.Single(shapes);
        // String literals and color/width operands must not corrupt path numbers.
        Assert.Equal(new VectorPoint(0, 0), poly.Points[0]);
        Assert.Equal(new VectorPoint(100, 100), poly.Points[1]);
        Assert.Equal(poly.Points[0], poly.Points[^1]);
    }

    [Fact]
    public void Newpath_StartsFreshSubpath()
    {
        const string eps = """
            %!PS-Adobe-3.0 EPSF-3.0
            %%BoundingBox: 0 0 100 100
            newpath
            0 0 moveto
            10 10 lineto
            newpath
            20 20 moveto
            30 30 lineto
            stroke
            """;

        var shapes = EpsImporter.Parse(eps);

        Assert.Equal(2, shapes.Count);
        Assert.Equal(new VectorPoint(0, 0), shapes[0].Points[0]);
        Assert.Equal(new VectorPoint(10, 10), shapes[0].Points[1]);
        Assert.Equal(new VectorPoint(20, 20), shapes[1].Points[0]);
        Assert.Equal(new VectorPoint(30, 30), shapes[1].Points[1]);
    }

    [Fact]
    public void GarbageInput_DoesNotThrow()
    {
        Assert.Empty(EpsImporter.Parse(""));
        Assert.Empty(EpsImporter.Parse("this is not an eps file at all"));
        Assert.Empty(EpsImporter.Parse("random text\nwithout magic header"));
    }
}
