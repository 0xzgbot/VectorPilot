using System.IO;
using VectorPilot.App;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class ToolpathSorterTests
{
    private static Toolpath Tp(string name, double depth, Guid? tool = null)
        => new() { Name = name, CutDepth = depth, ToolId = tool ?? Guid.Empty };

    [Fact]
    public void ByDepth_Shallow_First()
    {
        var list = new List<Toolpath> { Tp("deep", 5), Tp("shallow", 1), Tp("mid", 3) };
        var sorted = ToolpathSorter.Sort(list, ToolpathSortMode.ByDepth);
        Assert.Equal(new[] { "shallow", "mid", "deep" }, sorted.Select(t => t.Name).ToArray());
    }

    [Fact]
    public void ByName_Alphabetical()
    {
        var list = new List<Toolpath> { Tp("Zebra", 1), Tp("Apple", 1), Tp("Mango", 1) };
        var sorted = ToolpathSorter.Sort(list, ToolpathSortMode.ByName);
        Assert.Equal(new[] { "Apple", "Mango", "Zebra" }, sorted.Select(t => t.Name).ToArray());
    }

    [Fact]
    public void ToolChanges_Count_Group_Switches()
    {
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        var list = new List<Toolpath> { Tp("a", 1, t1), Tp("b", 1, t1), Tp("c", 1, t2), Tp("d", 1, t1) };
        Assert.Equal(2, ToolpathSorter.ToolChanges(list));
    }

    [Fact]
    public void MergeByTool_Groups_Contiguous()
    {
        var t1 = Guid.NewGuid();
        var t2 = Guid.NewGuid();
        var list = new List<Toolpath> { Tp("a", 1, t1), Tp("b", 1, t1), Tp("c", 1, t2), Tp("d", 1, t1) };
        var groups = ToolpathSorter.MergeByTool(list);
        Assert.Equal(3, groups.Count);
        Assert.Equal(2, groups[0].Count);
    }
}

public class MachineConfigDatabaseTests
{
    [Fact]
    public void Defaults_RoundTrip_And_Profile_Conversion()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vp-mach-{Guid.NewGuid():N}.json");
        try
        {
            var db = new MachineConfigDatabase(path).WithDefaults();
            Assert.True(db.Machines.Count >= 4);
            var reloaded = new MachineConfigDatabase(path);
            Assert.Equal(db.Machines.Count, reloaded.Machines.Count);

            var shapeoko = reloaded.Find("Shapeoko 3")!;
            var profile = shapeoko.ToProfile();
            Assert.Equal(500 / 25.4, profile.MaxX, 3);
            Assert.Equal("COM3", profile.PortName);
            Assert.False(profile.SupportsRotary);

            reloaded.Add(new MachineConfigEntry { Name = "My Rig", TravelXmm = 900 });
            Assert.NotNull(new MachineConfigDatabase(path).Find("My Rig"));
            Assert.True(reloaded.Delete("My Rig"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
