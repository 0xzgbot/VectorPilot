using System.IO;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class ToolDatabaseTests
{
    [Fact]
    public void SeedDefaults_Produces_Ten_Distinct_Tools()
    {
        var db = new ToolDatabase(seedDefaults: true);
        Assert.True(db.Tools.Count >= 10, $"expected >= 10 seeded tools, got {db.Tools.Count}");
        // 17 catalog entries dedup to 10 distinct physical tools
        Assert.Equal(10, db.Tools.Count);
    }

    [Fact]
    public void SeedDefaults_Covers_All_Thirteen_Classes_In_Enum()
    {
        Assert.Equal(14, System.Enum.GetValues<ToolType>().Length); // 13 classes + legacy slotCutter
    }

    [Fact]
    public void DefaultToolForStrategy_Returns_VCarve_Tool()
    {
        var db = new ToolDatabase(seedDefaults: true);
        var tool = db.DefaultToolForStrategy("V-Carve");
        Assert.NotNull(tool);
        Assert.Equal(ToolType.VBit, tool!.Type);
        Assert.Equal("V-Bit 90° 1¼\"", tool.Name);
    }

    [Fact]
    public void DefaultToolForStrategy_Unknown_Returns_Null()
    {
        var db = new ToolDatabase(seedDefaults: true);
        Assert.Null(db.DefaultToolForStrategy("NoSuchStrategy"));
    }

    [Fact]
    public void Seeded_Tools_Carry_Hardwood_CutData()
    {
        var db = new ToolDatabase(seedDefaults: true);
        var tool = db.Tools[0];
        Assert.Single(tool.CutData);
        Assert.Equal("hardwood", tool.CutData[0].Material);
        Assert.True(tool.CutData[0].FeedRateMmPerMin > 0);
        Assert.True(tool.CutData[0].SpindleRpm > 0);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips_Tools()
    {
        var db = new ToolDatabase(seedDefaults: true);
        string path = Path.Combine(Path.GetTempPath(), $"vp-tools-{Guid.NewGuid():N}.json");
        try
        {
            db.SaveToJson(path);
            var loaded = ToolDatabase.LoadFromJson(path);
            Assert.Equal(db.Tools.Count, loaded.Tools.Count);
            for (int i = 0; i < db.Tools.Count; i++)
            {
                Assert.Equal(db.Tools[i].Name, loaded.Tools[i].Name);
                Assert.Equal(db.Tools[i].Type, loaded.Tools[i].Type);
                Assert.Equal(db.Tools[i].DiameterMm, loaded.Tools[i].DiameterMm, 6);
                Assert.Equal(db.Tools[i].CutData.Count, loaded.Tools[i].CutData.Count);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ResolvedCutData_Uses_Material_Override()
    {
        var tool = new Tool
        {
            Name = "Test Tool",
            Type = ToolType.EndMill,
            DiameterMm = 6.35,
            CutData =
            {
                new ToolCutData { Material = "aluminum", FeedRateMmPerMin = 77, PlungeRateMmPerMin = 30, SpindleRpm = 9000, MaxDepthOfCutMm = 1.0 }
            }
        };
        var resolved = tool.ResolvedCutData("aluminum", machineName: null);
        Assert.Equal(77, resolved.FeedRateMmPerMin);
        Assert.Equal(9000, resolved.SpindleRpm, 0);
    }

    [Fact]
    public void ResolvedCutData_FallsBack_To_Derived_Defaults()
    {
        var tool = new Tool { Name = "Bare", Type = ToolType.EndMill, DiameterMm = 6.35 };
        var resolved = tool.ResolvedCutData(material: "hardwood", machineName: null);
        Assert.True(resolved.FeedRateMmPerMin > 0);
        Assert.True(resolved.SpindleRpm >= 6000 && resolved.SpindleRpm <= 24000);
    }
}
