using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>SPK-1132 parity: stock sheet presets.</summary>
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
            Assert.True(p.WidthMM > 0, $"{p.Name} width");
            Assert.True(p.DepthMM > 0, $"{p.Name} depth");
            Assert.True(p.ThicknessMM > 0, $"{p.Name} thickness");
        }
    }

    [Fact]
    public void Imperial_Dims_Are_Exact()
    {
        var fourByEight = StockSheetPresets.Imperial.First(p => p.Name.Contains("4'x8'") && p.Name.Contains("0.375"));
        Assert.Equal(1219.2, fourByEight.WidthMM, 4);
        Assert.Equal(2438.4, fourByEight.DepthMM, 4);
        Assert.Equal(9.525, fourByEight.ThicknessMM, 4);
    }

    [Fact]
    public void Metric_Dims_Are_Exact()
    {
        var m = StockSheetPresets.Metric.First(p => p.Name.StartsWith("1219x2438"));
        Assert.Equal(1219, m.WidthMM, 4);
        Assert.Equal(2438, m.DepthMM, 4);
    }

    [Fact]
    public void PresetByName_Finds_By_Key()
    {
        var p = StockSheetPresets.PresetByName("4'x8'x0.75''");
        Assert.NotNull(p);
        Assert.Equal(19.05, p!.ThicknessMM, 3);
    }

    [Fact]
    public void Apply_Sets_Sheet_Dims()
    {
        var sheet = new Sheet();
        var preset = StockSheetPresets.PresetByName("1219x2438x18 mm")!;
        StockSheetPresets.Apply(preset, sheet);
        Assert.Equal(1219, sheet.Width, 4);
        Assert.Equal(2438, sheet.Height, 4);
        Assert.Equal(18, sheet.Thickness, 4);
    }
}
