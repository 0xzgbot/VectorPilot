using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class ExporterTests
{
    private static List<VectorShape> Shapes() => new()
    {
        VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(10, 5)),
        VectorShape.Circle(new VectorPoint(1, 2), 3),
        VectorShape.Rectangle(0.5, 0.5, 4, 2)
    };

    [Fact]
    public void Dxf_Export_RoundTrips_Through_Importer()
    {
        var dxf = DxfExporter.DxfString(Shapes());
        var shapes = DxfImporter.Parse(dxf);
        Assert.Equal(3, shapes.Count);
        Assert.Contains(shapes, s => s.Type == ShapeType.Line);
        Assert.Contains(shapes, s => s.Type == ShapeType.Circle);
        Assert.Contains(shapes, s => s.Type == ShapeType.Polyline || s.Type == ShapeType.Rectangle);
    }

    [Fact]
    public void Stl_Ascii_Export_And_Reimport()
    {
        var tris = new List<StlImporter.Triangle>
        {
            new((0, 0, 0), (2, 0, 0), (0, 2, 1))
        };
        var ascii = MeshExporter.StlAscii(tris);
        var reparsed = StlImporter.Parse(System.Text.Encoding.UTF8.GetBytes(ascii));
        Assert.Single(reparsed);
    }

    [Fact]
    public void Stl_Binary_Export_RoundTrips()
    {
        var tris = new List<StlImporter.Triangle>
        {
            new((0, 0, 0), (2, 0, 0), (0, 2, 1)),
            new((2, 0, 0), (2, 2, 1), (0, 2, 1))
        };
        var bytes = MeshExporter.StlBinary(tris);
        var reparsed = StlImporter.Parse(bytes);
        Assert.Equal(2, reparsed.Count);
        double maxZ = Math.Max(reparsed[1].A.Z, Math.Max(reparsed[1].B.Z, reparsed[1].C.Z));
        Assert.Equal(1.0, maxZ, 3);
    }

    [Fact]
    public void Obj_Export_RoundTrips()
    {
        var tris = new List<StlImporter.Triangle>
        {
            new((0, 0, 0), (2, 0, 0), (0, 2, 1))
        };
        var obj = MeshExporter.ObjAscii(tris);
        var (reparsed, _, _) = ObjImporter.ParseAscii(obj);
        Assert.Single(reparsed);
    }

    [Fact]
    public void Heightfield_To_Triangles_Produces_Grid()
    {
        var hf = new HeightfieldData(3, 3, 1.0, 0, 0, new double[]
        {
            1, 1, 1,
            1, 2, 1,
            1, 1, 1
        });
        var tris = MeshExporter.HeightfieldToTriangles(hf);
        Assert.Equal(8, tris.Count); // 2x2 cells x 2 triangles
        Assert.All(tris, t => Assert.True(t.A.Z >= 0 && t.B.Z >= 0 && t.C.Z >= 0));
    }

    [Fact]
    public void Eps_Export_RoundTrips()
    {
        var eps = EpsExporter.EpsString(Shapes());
        Assert.StartsWith("%!PS-Adobe", eps);
        var shapes = EpsImporter.Parse(eps);
        Assert.NotEmpty(shapes);
    }

    [Fact]
    public void Pdf_Export_Is_Valid_And_RoundTrips()
    {
        var pdf = PdfExporter.PdfString(Shapes());
        Assert.StartsWith("%PDF-1.4", pdf);
        Assert.Contains("startxref", pdf);
        Assert.Contains("%%EOF", pdf);
        var shapes = PdfImporter.Parse(pdf);
        Assert.NotEmpty(shapes);
    }
}
