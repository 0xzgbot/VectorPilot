using System.IO;
using System.Text;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class StlImporterTests
{
    private static byte[] AsciiStl(string s) => Encoding.UTF8.GetBytes(s);

    [Fact]
    public void Ascii_Stl_Parses_Triangles()
    {
        const string stl = """
solid test
  facet normal 0 0 1
    outer loop
      vertex 0 0 0
      vertex 2 0 0
      vertex 0 2 0
    endloop
  endfacet
  facet normal 0 0 1
    outer loop
      vertex 2 0 0
      vertex 2 2 0
      vertex 0 2 0
    endloop
  endfacet
endsolid test
""";
        var tris = StlImporter.Parse(AsciiStl(stl));
        Assert.Equal(2, tris.Count);
    }

    [Fact]
    public void Binary_Stl_Parses()
    {
        using var ms = new MemoryStream();
        ms.Write(new byte[80]); // header
        ms.Write(BitConverter.GetBytes((uint)2)); // count
        void Tri(float x1, float y1, float z1, float x2, float y2, float z2, float x3, float y3, float z3)
        {
            for (int i = 0; i < 3; i++) ms.Write(BitConverter.GetBytes(0f)); // normal
            ms.Write(BitConverter.GetBytes(x1)); ms.Write(BitConverter.GetBytes(y1)); ms.Write(BitConverter.GetBytes(z1));
            ms.Write(BitConverter.GetBytes(x2)); ms.Write(BitConverter.GetBytes(y2)); ms.Write(BitConverter.GetBytes(z2));
            ms.Write(BitConverter.GetBytes(x3)); ms.Write(BitConverter.GetBytes(y3)); ms.Write(BitConverter.GetBytes(z3));
            ms.Write(BitConverter.GetBytes((ushort)0));
        }
        Tri(0, 0, 0, 1, 0, 0, 0, 1, 0);
        Tri(1, 0, 0, 1, 1, 0, 0, 1, 0);

        var tris = StlImporter.Parse(ms.ToArray());
        Assert.Equal(2, tris.Count);
        Assert.Equal(1.0f, (float)tris[1].A.X, 4);
    }

    [Fact]
    public void Rasterize_Flat_Triangle_Heights()
    {
        var tris = new List<StlImporter.Triangle>
        {
            new((0, 0, 5), (2, 0, 5), (0, 2, 5))
        };
        var grid = StlImporter.Rasterize(tris, cellSizeMm: 1.0, scale: 1.0);
        Assert.NotNull(grid);
        var h = grid.HeightAt(0.5, 0.5);
        Assert.NotNull(h);
        Assert.Equal(5.0, h!.Value, 6);
        Assert.Null(grid.HeightAt(5, 5)); // outside
    }

    [Fact]
    public void Rasterize_Slanted_Triangle_Interpolates_Z()
    {
        // Plane z = y through (0,0,0),(2,0,0),(0,2,2)
        var tris = new List<StlImporter.Triangle>
        {
            new((0, 0, 0), (2, 0, 0), (0, 2, 2))
        };
        var grid = StlImporter.Rasterize(tris, cellSizeMm: 1.0, scale: 1.0);
        var h = grid.HeightAt(0.5, 0.5);
        Assert.NotNull(h);
        Assert.Equal(0.5, h!.Value, 4);
    }

    [Fact]
    public void Import_Reports_Result()
    {
        const string stl = "solid t\nfacet normal 0 0 1\nouter loop\nvertex 0 0 0\nvertex 1 0 0\nvertex 0 1 0\nendloop\nendfacet\nendsolid t\n";
        var result = StlImporter.Import(AsciiStl(stl));
        Assert.True(result.Success);
        Assert.Equal(1, result.TriangleCount);
        Assert.NotNull(result.Heightfield);
    }

    [Fact]
    public void Empty_Input_Fails_Gracefully()
    {
        var result = StlImporter.Import(Array.Empty<byte>());
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }
}
