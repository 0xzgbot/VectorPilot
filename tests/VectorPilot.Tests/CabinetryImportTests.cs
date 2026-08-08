using System.IO;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class PartListImporterTests
{
    [Fact]
    public void Mozaik_CSV_Imports_Parts()
    {
        var csv = "PartNumber,PartName,Width,Height,Thickness,Qty,Material,Notes\n" +
                  "M-001,Side Panel,600,700,18,2,Birch Plywood,\n" +
                  "M-002,Shelf,580,300,18,4,Birch Plywood,\n";
        var parts = PartListImporter.Import(csv, PartListVendor.Mozaik);
        Assert.Equal(2, parts.Count);
        Assert.Equal("M-001", parts[0].Id);
        Assert.Equal(600, parts[0].WidthMm);
        Assert.Equal(18, parts[0].ThicknessMm);
        Assert.Equal(2, parts[0].Quantity);
        Assert.Equal("Birch Plywood", parts[0].Material);
    }

    [Fact]
    public void SmartWOP_TSV_Maps_Size_Columns()
    {
        var tsv = "PartNumber\tDescription\tXSize\tYSize\tZSize\tQuantity\tMaterial\n" +
                  "SW-1\tDrawer Front\t400\t150\t16\t3\tMDF\n";
        var parts = PartListImporter.Import(tsv, PartListVendor.SmartWOP);
        Assert.Single(parts);
        Assert.Equal(400, parts[0].WidthMm);
        Assert.Equal(150, parts[0].HeightMm);
        Assert.Equal(16, parts[0].ThicknessMm);
        Assert.Equal(3, parts[0].Quantity);
    }

    [Fact]
    public void Empty_And_Header_Only_Return_Empty()
    {
        Assert.Empty(PartListImporter.Import("", PartListVendor.Kcd));
        Assert.Empty(PartListImporter.Import("Width,Height,Qty\n", PartListVendor.Kcd));
    }

    [Fact]
    public void Schema_Loads_And_Applies()
    {
        var schema = PartListImporter.LoadMappingSchema("{\"columns\":{\"w\":\"WidthMm\",\"h\":\"HeightMm\",\"n\":\"Name\"}}");
        Assert.NotNull(schema);
        Assert.Equal("WidthMm", schema!["w"]);
    }
}

public class Crv3dTemplateTests
{
    [Fact]
    public void Round_Trip_Preserves_All()
    {
        var t = new Crv3dTemplate.JobTemplate
        {
            Name = "Cabinet Door",
            SheetWidthMm = 1220,
            MaterialThicknessMm = 18,
            Toolpaths =
            {
                new Crv3dTemplate.ToolpathTemplateEntry { Name = "Door Outline", Strategy = "profile", ParamsJson = "{\"cutMode\":\"outCut\"}" },
                new Crv3dTemplate.ToolpathTemplateEntry { Name = "Hardware Holes", Strategy = "drill", ParamsJson = "{}" }
            }
        };
        var json = Crv3dTemplate.Serialize(t);
        var back = Crv3dTemplate.Deserialize(json);
        Assert.NotNull(back);
        Assert.Equal("Cabinet Door", back!.Name);
        Assert.Equal(2, back.Toolpaths.Count);
        Assert.Equal("profile", back.Toolpaths[0].Strategy);
        Assert.Contains("\"outCut\"", back.Toolpaths[0].ParamsJson);
    }

    [Fact]
    public void Save_And_Load_File()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vp-template-{Guid.NewGuid():N}.json");
        try
        {
            Crv3dTemplate.Save(new Crv3dTemplate.JobTemplate { Name = "Sign Blank" }, path);
            var loaded = Crv3dTemplate.Load(path);
            Assert.NotNull(loaded);
            Assert.Equal("Sign Blank", loaded!.Name);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
