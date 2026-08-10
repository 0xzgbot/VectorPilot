using System.IO;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class DirtyRegionTests
{
    [Fact]
    public void Vector_Modification_Affects_Only_Related_Toolpaths()
    {
        var mgr = new DirtyRegionManager();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        mgr.MarkVectorModified(a);
        Assert.True(mgr.NeedsResimulation);
        Assert.True(mgr.Affects(new[] { a }));
        Assert.False(mgr.Affects(new[] { b }));
    }

    [Fact]
    public void Batch_And_Full_Tree_Coverage()
    {
        var mgr = new DirtyRegionManager();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        mgr.MarkBatchChange(new[] { a, b });
        Assert.True(mgr.Affects(new[] { b }));
        Assert.False(mgr.Affects(new[] { Guid.NewGuid() }));

        mgr.MarkFullTreeDirty();
        Assert.True(mgr.Affects(new[] { Guid.NewGuid() }));
    }

    [Fact]
    public void KeepOut_Zone_Affects_Everything()
    {
        var mgr = new DirtyRegionManager();
        mgr.MarkKeepOutZoneChanged();
        Assert.Equal(-1, mgr.DirtyRegions[0].AffectedCount);
        Assert.True(mgr.Affects(new[] { Guid.NewGuid() }));
    }

    [Fact]
    public void Clear_Resets_State()
    {
        var mgr = new DirtyRegionManager();
        mgr.MarkFullTreeDirty();
        mgr.Clear();
        Assert.False(mgr.NeedsResimulation);
        Assert.False(mgr.Affects(new[] { Guid.NewGuid() }));
    }

    [Fact]
    public void Empty_Batch_Is_Ignored()
    {
        var mgr = new DirtyRegionManager();
        mgr.MarkBatchChange(new List<Guid>());
        Assert.False(mgr.NeedsResimulation);
    }
}

public class UnifiedImportRouterTests
{
    [Fact]
    public void Format_From_Extension()
    {
        Assert.Equal(UnifiedImportRouter.Format.Svg, UnifiedImportRouter.FormatInfo.FromExtension("svg"));
        Assert.Equal(UnifiedImportRouter.Format.Dxf, UnifiedImportRouter.FormatInfo.FromExtension("DXF"));
        Assert.Equal(UnifiedImportRouter.Format.Dwg, UnifiedImportRouter.FormatInfo.FromExtension("dwg"));
        Assert.Null(UnifiedImportRouter.FormatInfo.FromExtension("xyz"));
        Assert.Equal("DWG", UnifiedImportRouter.FormatInfo.DisplayName(UnifiedImportRouter.Format.Dwg));
    }

    [Fact]
    public void Unknown_Extension_Returns_Warning()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vp-router-{Guid.NewGuid():N}.xyz");
        try
        {
            File.WriteAllText(path, "nope");
            var r = UnifiedImportRouter.ImportFile(path);
            Assert.Empty(r.Shapes);
            Assert.Contains(r.Warnings, w => w.Contains("Unsupported file extension"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Dxf_Imports_Through_Router()
    {
        var dxf = "0\nSECTION\n2\nENTITIES\n0\nLINE\n8\n0\n10\n0\n20\n0\n30\n0\n11\n10\n21\n0\n31\n0\n0\nENDSEC\n0\nEOF\n";
        var path = Path.Combine(Path.GetTempPath(), $"vp-router-{Guid.NewGuid():N}.dxf");
        try
        {
            File.WriteAllText(path, dxf);
            var r = UnifiedImportRouter.ImportFile(path);
            Assert.Equal(UnifiedImportRouter.Format.Dxf, r.Format);
            Assert.Single(r.Shapes);
            Assert.Empty(r.Warnings);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
