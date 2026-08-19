using System.IO;
using VectorPilot.App;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>Card A4 — tool browser: 3-part cut-data resolution, staged edits, save/revert.</summary>
public class ToolBrowserTests
{
    private static ToolDatabase Seeded() => new ToolDatabase(seedDefaults: true);

    [Fact]
    public void Catalog_Seeds_The_Installer_Verified_Set()
    {
        var vm = new ToolBrowserViewModel(Seeded());
        Assert.NotEmpty(vm.Tools);
        // 17 catalog entries collapse to distinct tools (several strategies share one).
        Assert.True(vm.Tools.Count >= 10, $"expected the shipped catalog, got {vm.Tools.Count}");
        Assert.NotEmpty(vm.Classes);
    }

    [Fact]
    public void Classes_Group_Tools_Without_Loss()
    {
        var vm = new ToolBrowserViewModel(Seeded());
        int viaClasses = vm.Classes.Sum(c => vm.ToolsOfClass(c).Count);
        Assert.Equal(vm.Tools.Count, viaClasses);
    }

    [Fact]
    public void Resolution_Falls_Back_To_Derived_Defaults()
    {
        var db = Seeded();
        var vm = new ToolBrowserViewModel(db) { Material = "unobtainium", MachineName = null };
        var tool = db.Tools[0];

        var r = vm.Resolve(tool);
        Assert.True(r.FeedRateMmPerMin > 0);
        Assert.True(r.SpindleRpm > 0);
        Assert.True(r.MaxDepthOfCutMm > 0);
    }

    [Fact]
    public void Material_Data_Beats_Derived_Defaults()
    {
        var db = Seeded();
        var tool = db.Tools[0];
        tool.CutData.Clear();
        tool.CutData.Add(new ToolCutData
        {
            Material = "oak", FeedRateMmPerMin = 1234, PlungeRateMmPerMin = 111,
            SpindleRpm = 9000, MaxDepthOfCutMm = 1.5
        });

        var vm = new ToolBrowserViewModel(db) { Material = "oak" };
        Assert.Equal(1234, vm.Resolve(tool).FeedRateMmPerMin, 6);
    }

    [Fact]
    public void Machine_Override_Beats_Material_Data()
    {
        var db = Seeded();
        var tool = db.Tools[0];
        tool.CutData.Clear();
        tool.CutData.Add(new ToolCutData { Material = "oak", FeedRateMmPerMin = 1234, SpindleRpm = 9000, MaxDepthOfCutMm = 1.5 });
        tool.MachineCutData.Add(new MachineCutData { MachineName = "BigRig", FeedRateMmPerMin = 4321, SpindleRpm = 18000, MaxDepthOfCutMm = 3 });

        var vm = new ToolBrowserViewModel(db) { Material = "oak", MachineName = "BigRig" };
        Assert.Equal(4321, vm.Resolve(tool).FeedRateMmPerMin, 6);   // machine wins

        vm.MachineName = null;
        Assert.Equal(1234, vm.Resolve(tool).FeedRateMmPerMin, 6);   // falls back to material
    }

    [Fact]
    public void Edit_Is_Staged_And_Does_Not_Touch_The_Database()
    {
        var db = Seeded();
        var tool = db.Tools[0];
        int cutCountBefore = tool.CutData.Count;

        var vm = new ToolBrowserViewModel(db) { Material = "hardwood" };
        vm.Edit(tool, 999, 400, 15000, 2.5);

        Assert.True(vm.IsDirty(tool));
        Assert.True(vm.HasPendingEdits);
        Assert.Equal(cutCountBefore, tool.CutData.Count);   // db untouched until Save
    }

    [Fact]
    public void Save_Commits_Staged_Edits()
    {
        var db = Seeded();
        var tool = db.Tools[0];
        var vm = new ToolBrowserViewModel(db) { Material = "hardwood" };

        vm.Edit(tool, 999, 400, 15000, 2.5);
        Assert.Equal(1, vm.Save());

        Assert.False(vm.HasPendingEdits);
        Assert.Equal(999, vm.Resolve(tool).FeedRateMmPerMin, 6);
    }

    [Fact]
    public void Save_Replaces_Rather_Than_Duplicating_A_Material_Row()
    {
        var db = Seeded();
        var tool = db.Tools[0];
        var vm = new ToolBrowserViewModel(db) { Material = "hardwood" };

        int before = tool.CutData.Count(c => c.Material.Equals("hardwood", StringComparison.OrdinalIgnoreCase));
        vm.Edit(tool, 777, 300, 12000, 2.0);
        vm.Save();
        int after = tool.CutData.Count(c => c.Material.Equals("hardwood", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(Math.Max(before, 1), after);   // exactly one row for that material
    }

    [Fact]
    public void Revert_Discards_Staged_Edits()
    {
        var db = Seeded();
        var tool = db.Tools[0];
        var vm = new ToolBrowserViewModel(db) { Material = "hardwood" };
        double original = vm.Resolve(tool).FeedRateMmPerMin;

        vm.Edit(tool, 5555, 400, 15000, 2.5);
        vm.Revert();

        Assert.False(vm.HasPendingEdits);
        Assert.False(vm.IsDirty(tool));
        Assert.Equal(original, vm.Resolve(tool).FeedRateMmPerMin, 6);
    }

    [Fact]
    public void PendingFor_Exposes_The_Staged_Row()
    {
        var db = Seeded();
        var tool = db.Tools[0];
        var vm = new ToolBrowserViewModel(db) { Material = "plastic" };

        Assert.Null(vm.PendingFor(tool));
        vm.Edit(tool, 2500, 800, 20000, 4);
        var staged = vm.PendingFor(tool)!;
        Assert.Equal("plastic", staged.Material);
        Assert.Equal(2500, staged.FeedRateMmPerMin, 6);
    }

    [Fact]
    public void Database_Round_Trips_Through_Json()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vp-tools-{Guid.NewGuid():N}.json");
        try
        {
            var db = Seeded();
            var vm = new ToolBrowserViewModel(db) { Material = "hardwood" };
            vm.Edit(db.Tools[0], 1357, 500, 16000, 2.2);
            vm.Save();
            vm.Database.SaveToJson(path);

            var reloaded = ToolDatabase.LoadFromJson(path);
            var vm2 = new ToolBrowserViewModel(reloaded) { Material = "hardwood" };
            Assert.Equal(1357, vm2.Resolve(reloaded.Tools[0]).FeedRateMmPerMin, 6);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
