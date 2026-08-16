using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>SPK-1132 parity: stock sheet presets.</summary>
public class StockSheetPresetTests
{
    [Fact]
    public void Catalog_Has_72_Presets()
    {
        Assert.Equal(72, StockSheetPresets.All.Count);
        Assert.Equal(36, StockSheetPresets.Imperial.Count);
        Assert.Equal(36, StockSheetPresets.Metric.Count);
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
    public void Apply_Sets_Sheet_Dims()
    {
        var sheet = new Sheet();
        var preset = StockSheetPresets.PresetByName("4'x8'x0.375''")!;
        StockSheetPresets.Apply(preset, sheet);
        Assert.Equal(1219.2, sheet.Width, 4);
        Assert.Equal(2438.4, sheet.Height, 4);
        Assert.Equal(9.525, sheet.Thickness, 4);
    }
}
