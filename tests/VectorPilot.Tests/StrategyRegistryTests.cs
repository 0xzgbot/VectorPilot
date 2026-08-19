using System.IO;
using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class StrategyRegistryTests
{
    private static readonly StrategyRegistry Registry = new();

    private static VectorShape Square() => VectorShape.Rectangle(0, 0, 10, 10);

    private static HeightfieldData Ridge()
    {
        var h = new double[64];
        for (int j = 0; j < 8; j++)
            for (int i = 0; i < 8; i++)
                h[j * 8 + i] = i < 4 ? 2 : 6;
        return new HeightfieldData(8, 8, 1.0, 0, 0, h);
    }

    [Fact]
    public void Registry_Covers_All_Strategies()
    {
        Assert.True(Registry.Entries.Count >= 19);
        Assert.NotNull(Registry.Find("profile"));
        Assert.NotNull(Registry.Find("rough3d"));
        Assert.NotNull(Registry.Find("laser-cut"));
    }

    [Fact]
    public void Defaults_Json_Is_Valid_For_Every_Entry()
    {
        foreach (var entry in Registry.Entries)
        {
            var result = entry.Compute(new[] { Square() }, Ridge(), entry.DefaultsJson);
            // Every strategy must produce usable output from its defaults.
            Assert.True(result.Gcode.Count >= 3, $"{entry.Key} produced {result.Gcode.Count} lines");
            Assert.Contains(result.Gcode, l => l.StartsWith("G0") || l.StartsWith("G1"));
        }
    }

    [Fact]
    public void Heightfield_Strategies_Need_A_Heightfield()
    {
        var rough = Registry.Find("rough3d")!;
        var withHf = rough.Compute(new[] { Square() }, Ridge(), rough.DefaultsJson);
        Assert.Contains(withHf.Gcode, l => l.StartsWith("G0"));
        var without = rough.Compute(new[] { Square() }, null, rough.DefaultsJson);
        // No relief must yield NO program plus a reason. This previously asserted the
        // presence of a "(No heightfield loaded)" line — i.e. it pinned a runnable
        // two-line stub that streamed to the machine as a successful no-op cut.
        Assert.Empty(without.Gcode);
        Assert.False(string.IsNullOrWhiteSpace(without.Error));
    }

    [Fact]
    public void Param_Override_Changes_Output()
    {
        var profile = Registry.Find("profile")!;
        var shallow = profile.Compute(new[] { Square() }, null, "{\"maxDepthOfCutMm\":1}");
        var deep = profile.Compute(new[] { Square() }, null, "{\"maxDepthOfCutMm\":5}");
        Assert.Contains(shallow.Gcode, l => l.Contains("Z-1.000"));
        Assert.Contains(deep.Gcode, l => l.Contains("Z-5.000"));
    }
}

public class ImportHubTests
{
    [Fact]
    public void Describe_Knows_All_Formats()
    {
        Assert.Equal("DXF", ImportHub.Describe("part.dxf"));
        Assert.Equal("STL", ImportHub.Describe("part.stl"));
        Assert.Equal("unknown", ImportHub.Describe("part.xyz"));
    }

    [Fact]
    public void Dxf_Import_Produces_Shapes()
    {
        var dxf = "0\nSECTION\n2\nENTITIES\n0\nLINE\n8\n0\n10\n0\n20\n0\n30\n0\n11\n10\n21\n0\n31\n0\n0\nENDSEC\n0\nEOF\n";
        var path = Path.Combine(Path.GetTempPath(), $"vp-import-{Guid.NewGuid():N}.dxf");
        try
        {
            File.WriteAllText(path, dxf);
            var r = ImportHub.Import(path);
            Assert.Equal("DXF", r.Format);
            Assert.Single(r.Shapes);
            Assert.Equal(10.0, r.Shapes[0].Points[1].X, 6);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Unknown_Format_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vp-import-{Guid.NewGuid():N}.xyz");
        try
        {
            File.WriteAllText(path, "nope");
            Assert.Throws<NotSupportedException>(() => ImportHub.Import(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
