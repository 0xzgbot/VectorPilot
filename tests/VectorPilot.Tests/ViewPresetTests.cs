using VectorPilot.App;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Named view presets (Mac SPK-UXPOLISH parity). Fit presets resolve against the
/// live sheet so they stay correct when stock size changes; built-ins are protected.
/// </summary>
public class ViewPresetTests
{
    [Fact]
    public void Ships_The_Built_In_Presets()
    {
        var m = new ViewPresetModel();
        Assert.Equal(4, m.Presets.Count);
        Assert.NotNull(m.Find("Fit sheet"));
        Assert.NotNull(m.Find("Actual size (100%)"));
        Assert.NotNull(m.Find("Zoom 200%"));
        Assert.NotNull(m.Find("Zoom 50%"));
    }

    [Fact]
    public void Find_Is_Case_Insensitive()
    {
        var m = new ViewPresetModel();
        Assert.NotNull(m.Find("fit sheet"));
        Assert.NotNull(m.Find("FIT SHEET"));
        Assert.Null(m.Find("no such preset"));
    }

    [Fact]
    public void Fixed_Zoom_Presets_Return_Their_Factor()
    {
        var m = new ViewPresetModel();
        double z = ViewPresetModel.ResolveZoom(m.Find("Zoom 200%")!, 800, 600, 400, 300);
        Assert.Equal(2.0, z, 6);
    }

    [Fact]
    public void Fit_Scales_The_Sheet_Into_The_Viewport()
    {
        var m = new ViewPresetModel();
        // 800x600 viewport, 400x300 sheet: limiting factor is 2.0, minus an 8% margin.
        double z = ViewPresetModel.ResolveZoom(m.Find("Fit sheet")!, 800, 600, 400, 300);
        Assert.Equal(1.84, z, 3);
    }

    [Fact]
    public void Fit_Uses_The_Tighter_Axis()
    {
        var m = new ViewPresetModel();
        // Wide viewport, tall sheet: height constrains.
        double z = ViewPresetModel.ResolveZoom(m.Find("Fit sheet")!, 2000, 400, 100, 200);
        Assert.Equal(400.0 / 200.0 * 0.92, z, 6);
    }

    [Fact]
    public void Fit_Is_Safe_With_A_Degenerate_Sheet_Or_Viewport()
    {
        var m = new ViewPresetModel();
        var fit = m.Find("Fit sheet")!;
        Assert.Equal(1.0, ViewPresetModel.ResolveZoom(fit, 800, 600, 0, 300), 6);
        Assert.Equal(1.0, ViewPresetModel.ResolveZoom(fit, 0, 0, 400, 300), 6);
    }

    [Fact]
    public void Saving_Captures_Zoom_And_Pan()
    {
        var m = new ViewPresetModel();
        var p = m.Save("My view", 1.75, 120, -40);

        Assert.Equal(5, m.Presets.Count);
        Assert.Equal(1.75, p.Zoom, 6);
        Assert.Equal(120, p.PanX, 6);
        Assert.Equal(-40, p.PanY, 6);
    }

    [Fact]
    public void Saving_The_Same_Name_Replaces_The_User_Preset()
    {
        var m = new ViewPresetModel();
        m.Save("My view", 1.0, 0, 0);
        m.Save("My view", 3.0, 10, 10);

        Assert.Equal(5, m.Presets.Count);           // not duplicated
        Assert.Equal(3.0, m.Find("My view")!.Zoom, 6);
    }

    [Fact]
    public void Built_Ins_Cannot_Be_Removed()
    {
        var m = new ViewPresetModel();
        Assert.False(m.Remove("Fit sheet"));
        Assert.Equal(4, m.Presets.Count);
    }

    [Fact]
    public void User_Presets_Can_Be_Removed()
    {
        var m = new ViewPresetModel();
        m.Save("Temp", 1.2, 0, 0);
        Assert.True(m.Remove("Temp"));
        Assert.Equal(4, m.Presets.Count);
        Assert.False(m.Remove("Temp"));   // already gone
    }
}
