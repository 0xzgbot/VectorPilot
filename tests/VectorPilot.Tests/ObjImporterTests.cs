using System.Text;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class ObjImporterTests
{
    [Fact]
    public void Obj_Parses_Vertices_And_Faces()
    {
        const string obj = """
# a quad
v 0 0 0
v 2 0 0
v 2 2 0
v 0 2 0
f 1 2 3 4
""";
        var (tris, vertexCount, faceCount) = ObjImporter.ParseAscii(obj);
        Assert.Equal(4, vertexCount);
        Assert.Equal(1, faceCount);
        Assert.Equal(2, tris.Count); // fan triangulation
    }

    [Fact]
    public void Obj_Handles_Slashed_And_Negative_Indices()
    {
        const string obj = """
v 0 0 0
v 1 0 0
v 0 1 0
f 1/1/1 2/2/2 3/3/3
v 2 0 0
v 2 1 0
v 1 1 0
f -3 -2 -1
""";
        var (tris, vertexCount, faceCount) = ObjImporter.ParseAscii(obj);
        Assert.Equal(6, vertexCount);
        Assert.Equal(2, faceCount);
        Assert.Equal(2, tris.Count);
    }

    [Fact]
    public void Obj_Import_Rasterizes()
    {
        const string obj = "v 0 0 0\nv 2 0 0\nv 0 2 2\nf 1 2 3\n";
        var result = ObjImporter.Import(Encoding.UTF8.GetBytes(obj));
        Assert.True(result.Success);
        Assert.Equal(1, result.TriangleCount);
        Assert.NotNull(result.Heightfield);
    }

    [Fact]
    public void Obj_Rejects_Binary()
    {
        var result = ObjImporter.Import(new byte[] { 1, 0, 2, 0, 3, 0 });
        Assert.False(result.Success);
        Assert.Contains("Binary", result.ErrorMessage);
    }

    [Fact]
    public void Obj_No_Faces_Fails_Gracefully()
    {
        var result = ObjImporter.Import(Encoding.UTF8.GetBytes("v 0 0 0\nv 1 1 1\n"));
        Assert.False(result.Success);
        Assert.Contains("no valid faces", result.ErrorMessage);
    }
}
