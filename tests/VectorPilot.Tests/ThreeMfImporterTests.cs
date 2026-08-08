using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class ThreeMfImporterTests
{
    private const string Ns = "http://schemas.microsoft.com/3dmanufacturing/core/2015/02";

    private static byte[] Build3Mf(XDocument model)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("3D/3dmodel.model");
            using var w = new StreamWriter(entry.Open(), Encoding.UTF8);
            w.Write(model.ToString());
        }
        return ms.ToArray();
    }

    private static XDocument BoxModel()
    {
        XNamespace ns = Ns;
        return new XDocument(
            new XElement(ns + "model",
                new XAttribute("unit", "millimeter"),
                new XElement(ns + "resources",
                    new XElement(ns + "object",
                        new XAttribute("id", "1"),
                        new XElement(ns + "mesh",
                            new XElement(ns + "vertices",
                                new XElement(ns + "vertex", new XAttribute("x", "0"), new XAttribute("y", "0"), new XAttribute("z", "0")),
                                new XElement(ns + "vertex", new XAttribute("x", "2"), new XAttribute("y", "0"), new XAttribute("z", "0")),
                                new XElement(ns + "vertex", new XAttribute("x", "0"), new XAttribute("y", "2"), new XAttribute("z", "0")),
                                new XElement(ns + "vertex", new XAttribute("x", "2"), new XAttribute("y", "2"), new XAttribute("z", "0"))),
                            new XElement(ns + "triangles",
                                new XElement(ns + "triangle", new XAttribute("v1", "0"), new XAttribute("v2", "1"), new XAttribute("v3", "2")),
                                new XElement(ns + "triangle", new XAttribute("v1", "1"), new XAttribute("v2", "3"), new XAttribute("v3", "2"))))))));
    }

    [Fact]
    public void ParseModelXml_Collects_Vertices_And_Triangles()
    {
        var (vertices, refs) = ThreeMfImporter.ParseModelXml(BoxModel().ToString());
        Assert.Equal(4, vertices.Count);
        Assert.Equal(2, refs.Count);
        Assert.Equal((0, 1, 2), refs[0]);
    }

    [Fact]
    public void Import_From_Zip_Succeeds()
    {
        var result = ThreeMfImporter.Import(Build3Mf(BoxModel()));
        Assert.True(result.Success);
        Assert.Equal(2, result.TriangleCount);
        Assert.NotNull(result.Heightfield);
    }

    [Fact]
    public void Import_Rejects_Non_Zip()
    {
        var result = ThreeMfImporter.Import(Encoding.UTF8.GetBytes("not a zip"));
        Assert.False(result.Success);
        Assert.Contains("ZIP", result.ErrorMessage);
    }

    [Fact]
    public void Import_Missing_Model_Entry_Fails()
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("README.txt");
            using var w = new StreamWriter(entry.Open());
            w.Write("hi");
        }
        var result = ThreeMfImporter.Import(ms.ToArray());
        Assert.False(result.Success);
        Assert.Contains("3dmodel.model", result.ErrorMessage);
    }
}
