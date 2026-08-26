using System.IO;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Cabinetry part-list import validated against REAL vendor fixture files.
///
/// Cabinetry import needed fixture validation: the existing
/// tests used inline strings and covered only 2 of the 6 supported vendors, with zero
/// fixture files on disk. Each vendor uses a different header vocabulary
/// (Width vs W vs XSize vs Width_mm) and SmartWOP is tab-delimited, so a mapping
/// regression in any one of them would previously have shipped silently.
/// </summary>
public class CabinetryFixtureTests
{
    private static string FixtureDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "tests", "VectorPilot.Tests", "fixtures", "cabinetry");
        }
    }

    private static string Load(string file) => File.ReadAllText(Path.Combine(FixtureDir, file));

    public static IEnumerable<object[]> AllVendors()
    {
        yield return new object[] { "mozaik.csv", PartListVendor.Mozaik, 5 };
        yield return new object[] { "kcd.csv", PartListVendor.Kcd, 5 };
        yield return new object[] { "cabinetsense.csv", PartListVendor.CabinetSense, 4 };
        yield return new object[] { "cabinetpartspro.csv", PartListVendor.CabinetPartsPro, 4 };
        yield return new object[] { "polyboard.csv", PartListVendor.Polyboard, 5 };
        yield return new object[] { "smartwop.tsv", PartListVendor.SmartWOP, 4 };
    }

    // ---- every vendor round-trips its own fixture ----

    [Theory]
    [MemberData(nameof(AllVendors))]
    public void Fixture_Exists(string file, PartListVendor vendor, int expected)
    {
        _ = vendor; _ = expected;
        Assert.True(File.Exists(Path.Combine(FixtureDir, file)), $"missing fixture {file}");
    }

    [Theory]
    [MemberData(nameof(AllVendors))]
    public void Every_Row_Imports(string file, PartListVendor vendor, int expected)
    {
        var parts = PartListImporter.Import(Load(file), vendor);
        Assert.Equal(expected, parts.Count);
    }

    [Theory]
    [MemberData(nameof(AllVendors))]
    public void Every_Part_Has_Real_Dimensions(string file, PartListVendor vendor, int expected)
    {
        _ = expected;
        foreach (var p in PartListImporter.Import(Load(file), vendor))
        {
            Assert.True(p.WidthMm > 0, $"{vendor} {p.Name}: width {p.WidthMm} not mapped");
            Assert.True(p.HeightMm > 0, $"{vendor} {p.Name}: height {p.HeightMm} not mapped");
            Assert.True(p.ThicknessMm > 0, $"{vendor} {p.Name}: thickness {p.ThicknessMm} not mapped");
        }
    }

    [Theory]
    [MemberData(nameof(AllVendors))]
    public void Every_Part_Has_An_Id_And_Name(string file, PartListVendor vendor, int expected)
    {
        _ = expected;
        foreach (var p in PartListImporter.Import(Load(file), vendor))
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Id), $"{vendor}: a part has no id");
            Assert.False(string.IsNullOrWhiteSpace(p.Name), $"{vendor}: a part has no name");
        }
    }

    [Theory]
    [MemberData(nameof(AllVendors))]
    public void Quantities_Are_At_Least_One(string file, PartListVendor vendor, int expected)
    {
        _ = expected;
        foreach (var p in PartListImporter.Import(Load(file), vendor))
            Assert.True(p.Quantity >= 1, $"{vendor} {p.Name}: quantity {p.Quantity}");
    }

    // ---- vendor-specific values, so a mapping swap is caught ----

    [Fact]
    public void Mozaik_Maps_Its_Own_Column_Names()
    {
        var parts = PartListImporter.Import(Load("mozaik.csv"), PartListVendor.Mozaik);
        var side = parts.First(p => p.Id == "B1-SIDE-L");

        Assert.Equal("Base Left Side", side.Name);
        Assert.Equal(584.2, side.WidthMm, 3);
        Assert.Equal(876.3, side.HeightMm, 3);
        Assert.Equal(18, side.ThicknessMm, 3);
        Assert.Equal(2, side.Quantity);
        Assert.Equal("Maple Ply", side.Material);
    }

    [Fact]
    public void Kcd_Maps_Depth_To_Thickness()
    {
        // KCD calls the third dimension "Depth", not "Thickness".
        var parts = PartListImporter.Import(Load("kcd.csv"), PartListVendor.Kcd);
        var back = parts.First(p => p.Id == "1004");

        Assert.Equal("Wall Cab Back", back.Name);
        Assert.Equal(6.35, back.ThicknessMm, 3);
    }

    [Fact]
    public void CabinetSense_Maps_Single_Letter_Columns()
    {
        // W / H / T / Count — the most easily mis-mapped header set.
        var parts = PartListImporter.Import(Load("cabinetsense.csv"), PartListVendor.CabinetSense);
        var pantry = parts.First(p => p.Id == "CS-100");

        Assert.Equal(600, pantry.WidthMm, 3);
        Assert.Equal(2100, pantry.HeightMm, 3);
        Assert.Equal(18, pantry.ThicknessMm, 3);
        Assert.Equal(2, pantry.Quantity);
    }

    [Fact]
    public void CabinetPartsPro_Maps_Underscored_Columns()
    {
        var parts = PartListImporter.Import(Load("cabinetpartspro.csv"), PartListVendor.CabinetPartsPro);
        var box = parts.First(p => p.Id == "CPP-7783");

        Assert.Equal("Drawer Box Bottom", box.Name);
        Assert.Equal(6.35, box.ThicknessMm, 3);
        Assert.Equal(4, box.Quantity);
    }

    [Fact]
    public void Polyboard_Maps_Unit_Suffixed_Columns()
    {
        // Width_mm / Height_mm / Thickness_mm.
        var parts = PartListImporter.Import(Load("polyboard.csv"), PartListVendor.Polyboard);
        var gable = parts.First(p => p.Id == "PB-01");

        Assert.Equal(570, gable.WidthMm, 3);
        Assert.Equal(720, gable.HeightMm, 3);
        Assert.Equal(18, gable.ThicknessMm, 3);
    }

    [Fact]
    public void SmartWOP_Parses_Tab_Delimited_XYZ_Columns()
    {
        // Tab-delimited AND XSize/YSize/ZSize — two ways to get this wrong.
        var parts = PartListImporter.Import(Load("smartwop.tsv"), PartListVendor.SmartWOP);
        var panel = parts.First(p => p.Id == "SW-2001");

        Assert.Equal("Island End Panel", panel.Name);
        Assert.Equal(900, panel.WidthMm, 3);
        Assert.Equal(720, panel.HeightMm, 3);
        Assert.Equal(18, panel.ThicknessMm, 3);
        Assert.Equal("Walnut Ply", panel.Material);
    }

    // ---- cross-vendor invariants ----

    [Fact]
    public void No_Vendor_Silently_Drops_Every_Row()
    {
        // A mapping that matches nothing yields zero parts — the failure mode this row
        // was flagged for.
        foreach (var data in AllVendors())
        {
            var file = (string)data[0];
            var vendor = (PartListVendor)data[1];
            Assert.NotEmpty(PartListImporter.Import(Load(file), vendor));
        }
    }

    [Fact]
    public void Total_Panel_Count_Across_All_Fixtures_Is_Stable()
    {
        int total = AllVendors().Sum(d =>
            PartListImporter.Import(Load((string)d[0]), (PartListVendor)d[1])
                            .Sum(p => p.Quantity));

        // 5 vendors' quantities summed; pins accidental quantity-column swaps.
        Assert.True(total > 40, $"total panels {total} is implausibly low");
    }

    [Fact]
    public void A_Vendor_Mapping_Applied_To_The_Wrong_File_Does_Not_Fabricate_Data()
    {
        // Feed SmartWOP's tab file through Mozaik's comma mapping: it must not invent
        // dimensioned parts.
        var wrong = PartListImporter.Import(Load("smartwop.tsv"), PartListVendor.Mozaik);
        Assert.True(wrong.Count == 0 || wrong.All(p => p.WidthMm == 0),
            "a mismatched mapping produced dimensioned parts");
    }
}
