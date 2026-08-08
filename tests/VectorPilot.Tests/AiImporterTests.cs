using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class AiImporterTests
{
    [Fact]
    public void PdfFlavor_DelegatesToPdfImporter()
    {
        const string ai = """
            %PDF-1.4
            1 0 obj
            << /Length 30 >>
            stream
            0 0 m
            100 0 l
            100 100 l
            h
            f
            endstream
            endobj
            %%EOF
            """;

        var shapes = AiImporter.Parse(ai);

        var poly = Assert.Single(shapes);
        Assert.Equal(ShapeType.Polyline, poly.Type);
        Assert.Equal(4, poly.Points.Count);
        Assert.Equal(new VectorPoint(100, 100), poly.Points[2]);
    }

    [Fact]
    public void EpsFlavor_DelegatesToEpsImporter()
    {
        const string ai = """
            %!PS-Adobe-3.0 EPSF-3.0
            %%BoundingBox: 0 0 100 100
            newpath
            0 0 moveto
            100 0 lineto
            100 100 lineto
            closepath
            stroke
            """;

        var shapes = AiImporter.Parse(ai);

        var poly = Assert.Single(shapes);
        Assert.Equal(ShapeType.Polyline, poly.Type);
        Assert.True(poly.Closed);
        Assert.True(poly.Points.Count >= 3);
        Assert.Equal(poly.Points[0], poly.Points[^1]);
    }

    [Fact]
    public void UnsupportedFlavor_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => AiImporter.Parse("this is not an illustrator file"));
        Assert.Throws<FormatException>(() => AiImporter.Parse(""));
        Assert.Throws<FormatException>(() => AiImporter.Parse("PNG\nsome other binary format"));
    }
}
