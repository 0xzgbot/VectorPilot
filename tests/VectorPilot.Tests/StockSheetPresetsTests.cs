using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class StockSheetPresetsTests
{
    [Fact]
    public void Catalog_Has_72_Presets()
    {
        Assert.Equal(72, StockSheetPresets.All.Count);
        Assert.Equal(36, StockSheetPresets.Imperial.Count);
        Assert.Equal(36, StockSheetPresets.Metric.Count);
    }

    [Fact]
    public void All_Presets_Have_Positive_Dimensions()
    {
        foreach (var p in StockSheetPresets.All)
        {
            Assert.True(p.WidthMm > 0, $"{p.Name} width");
            Assert.True(p.DepthMm > 0, $"{p.Name} depth");
            Assert.True(p.ThicknessMm > 0, $"{p.Name} thickness");
        }
    }

    [Fact]
    public void Imperial_Names_And_Order_Are_Stable()
    {
        Assert.Equal("2'x2'x0.125''", StockSheetPresets.Imperial[0].Name);
        Assert.Equal("8'x4'x1''", StockSheetPresets.Imperial[^1].Name);
    }

    [Fact]
    public void Metric_Names_Use_Mm()
    {
        Assert.Equal("2438x1219x12 mm", StockSheetPresets.Metric[^3].Name);
    }

    [Fact]
    public void PresetNamed_Finds_By_Key()
    {
        var p = StockSheetPresets.PresetNamed("4'x8'x0.75''");
        Assert.NotNull(p);
        Assert.Equal(1219.2, p!.WidthMm, 3);
        Assert.Equal(2438.4, p.DepthMm, 3);
        Assert.Equal(19.05, p.ThicknessMm, 3);
    }

    [Fact]
    public void Apply_Sets_Sheet_Fields()
    {
        var sheet = new Sheet();
        var preset = StockSheetPresets.PresetNamed("1219x2438x18 mm")!;
        StockSheetPresets.Apply(preset, sheet);
        Assert.Equal("1219x2438x18 mm", sheet.Name);
        Assert.Equal(1219, sheet.Width);
        Assert.Equal(2438, sheet.Height);
        Assert.Equal(18, sheet.Thickness);
        Assert.Equal(UnitSystem.Millimeters, sheet.Units);
    }
}
