using System.IO;
using System.IO.Compression;
using System.Text;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class PdfImporterTests
{
    [Fact]
    public void PlainContentStream_MovesLinesAndCloses_ProducesTrianglePolyline()
    {
        const string pdf = """
            %PDF-1.4
            1 0 obj
            << /Type /Page /Contents 2 0 R >>
            endobj
            2 0 obj
            << /Length 45 >>
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

        var shapes = PdfImporter.Parse(pdf);

        var poly = Assert.Single(shapes);
        Assert.Equal(ShapeType.Polyline, poly.Type);
        // 3 vertices + the start point re-appended by h.
        Assert.Equal(4, poly.Points.Count);
        Assert.Equal(new VectorPoint(0, 0), poly.Points[0]);
        Assert.Equal(new VectorPoint(100, 0), poly.Points[1]);
        Assert.Equal(new VectorPoint(100, 100), poly.Points[2]);
        Assert.Equal(poly.Points[0], poly.Points[^1]);
        Assert.True(poly.Closed);
    }

    [Fact]
    public void FlateDecodeStream_IsInflatedAndParsed()
    {
        var compressed = Deflate("0 0 m\n100 0 l\n100 100 l\n0 100 l\nh\nf\n");
        var payload = Encoding.Latin1.GetString(compressed);
        var pdf = "%PDF-1.4\n1 0 obj\n<< /Length " + compressed.Length
                  + " /Filter /FlateDecode >>\nstream\n" + payload
                  + "\nendstream\nendobj\n%%EOF\n";

        var shapes = PdfImporter.Parse(pdf);

        var poly = Assert.Single(shapes);
        Assert.Equal(ShapeType.Polyline, poly.Type);
        // 4 vertices + the start point re-appended by h.
        Assert.Equal(5, poly.Points.Count);
        Assert.Equal(new VectorPoint(0, 0), poly.Points[0]);
        Assert.Equal(new VectorPoint(100, 0), poly.Points[1]);
        Assert.Equal(new VectorPoint(100, 100), poly.Points[2]);
        Assert.Equal(new VectorPoint(0, 100), poly.Points[3]);
        Assert.Equal(poly.Points[0], poly.Points[^1]);
        Assert.True(poly.Closed);
    }

    [Fact]
    public void CmTransform_AppliesMatrixToPoints()
    {
        // 2 0 0 2 10 10 cm doubles coordinates and translates by (10, 10).
        const string pdf = """
            %PDF-1.4
            1 0 obj
            << /Length 40 >>
            stream
            2 0 0 2 10 10 cm
            0 0 m
            5 5 l
            S
            endstream
            endobj
            %%EOF
            """;

        var shapes = PdfImporter.Parse(pdf);

        var poly = Assert.Single(shapes);
        Assert.Equal(new VectorPoint(10, 10), poly.Points[0]);
        Assert.Equal(new VectorPoint(20, 20), poly.Points[1]);
    }

    [Fact]
    public void QandQ_RestoreCtmStack()
    {
        const string pdf = """
            %PDF-1.4
            1 0 obj
            << /Length 60 >>
            stream
            q
            2 0 0 2 0 0 cm
            1 1 m
            2 2 l
            Q
            3 3 m
            4 4 l
            S
            endstream
            endobj
            %%EOF
            """;

        var shapes = PdfImporter.Parse(pdf);

        var poly = Assert.Single(shapes);
        // Only the second subpath survives the painting operator; Q restored
        // the identity CTM, so points come back untransformed.
        Assert.Equal(new VectorPoint(3, 3), poly.Points[0]);
        Assert.Equal(new VectorPoint(4, 4), poly.Points[1]);
    }

    [Fact]
    public void RectOperator_ProducesRectangleShape()
    {
        const string pdf = """
            %PDF-1.4
            1 0 obj
            << /Length 20 >>
            stream
            10 20 30 40 re
            S
            endstream
            endobj
            %%EOF
            """;

        var shapes = PdfImporter.Parse(pdf);

        var rect = Assert.Single(shapes);
        Assert.Equal(ShapeType.Rectangle, rect.Type);
        Assert.Equal(30, rect.Bounds().Width);
        Assert.Equal(40, rect.Bounds().Height);
    }

    [Fact]
    public void TextOperators_AreSkipped()
    {
        const string pdf = """
            %PDF-1.4
            1 0 obj
            << /Length 60 >>
            stream
            BT
            /F1 24 Tf
            72 720 Td
            (Hello) Tj
            ET
            0 0 m
            50 50 l
            S
            endstream
            endobj
            %%EOF
            """;

        var shapes = PdfImporter.Parse(pdf);

        var poly = Assert.Single(shapes);
        Assert.Equal(new VectorPoint(0, 0), poly.Points[0]);
        Assert.Equal(new VectorPoint(50, 50), poly.Points[1]);
    }

    [Fact]
    public void GarbageInput_DoesNotThrow()
    {
        Assert.Empty(PdfImporter.Parse(""));
        Assert.Empty(PdfImporter.Parse("this is not a pdf file at all"));
        Assert.Empty(PdfImporter.Parse("%PDF-1.4\n1 0 obj\n<< /Length 10 >>\nno streams here\n%%EOF"));
    }

    /// <summary>RFC-1950 zlib compress (the FlateDecode format) for test fixtures.</summary>
    private static byte[] Deflate(string text)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            zlib.Write(bytes, 0, bytes.Length);
        }
        return output.ToArray();
    }
}
