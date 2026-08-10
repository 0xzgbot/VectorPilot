using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class ToolpathResimPlannerTests
{
    private static Toolpath Tp(string name, double depth, params Guid[] shapeIds)
    {
        var tp = new Toolpath { Name = name, CutDepth = depth };
        foreach (var id in shapeIds) tp.SelectedShapeIds.Add(id);
        return tp;
    }

    [Fact]
    public void AffectedToolpaths_Returns_Only_Toolpaths_On_Dirty_Vector()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var toolpaths = new List<Toolpath>
        {
            Tp("uses-a", 1, a),
            Tp("uses-b", 1, b),
            Tp("uses-both", 1, a, b),
            Tp("unrelated", 1, c)
        };
        var mgr = new DirtyRegionManager();
        mgr.MarkVectorModified(b);

        var affected = ToolpathResimPlanner.AffectedToolpaths(toolpaths, mgr);

        Assert.Equal(new[] { "uses-b", "uses-both" }, affected.Select(t => t.Name).ToArray());
    }

    [Fact]
    public void MarkFullTreeDirty_Affects_All_Toolpaths()
    {
        var toolpaths = new List<Toolpath>
        {
            Tp("one", 1, Guid.NewGuid()),
            Tp("two", 1, Guid.NewGuid())
        };
        var mgr = new DirtyRegionManager();
        mgr.MarkFullTreeDirty();

        var affected = ToolpathResimPlanner.AffectedToolpaths(toolpaths, mgr);

        Assert.Equal(2, affected.Count);
        Assert.Equal(new[] { "one", "two" }, affected.Select(t => t.Name).ToArray());
    }

    [Fact]
    public void SortForCut_ByDepth_Orders_Shallowest_First()
    {
        var toolpaths = new List<Toolpath>
        {
            Tp("deep", 5),
            Tp("mid", 3),
            Tp("shallow", 1)
        };

        var sorted = ToolpathResimPlanner.SortForCut(toolpaths, ToolpathSortMode.ByDepth);

        Assert.Equal(new[] { "shallow", "mid", "deep" }, sorted.Select(t => t.Name).ToArray());
    }
}
