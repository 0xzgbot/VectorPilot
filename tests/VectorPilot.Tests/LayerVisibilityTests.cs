using VectorPilot.App;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Layer visibility chips (Mac SPK-UXPOLISH parity). Solo must be non-destructive:
/// clearing it restores exactly the visibility the user had before.
/// </summary>
public class LayerVisibilityTests
{
    private static Sheet ThreeLayers()
    {
        var sheet = new Sheet { Name = "S", Width = 100, Height = 100, Thickness = 18 };
        sheet.Layers.Clear();
        sheet.Layers.Add(new Layer { Name = "A", Visible = true });
        sheet.Layers.Add(new Layer { Name = "B", Visible = true });
        sheet.Layers.Add(new Layer { Name = "C", Visible = false });   // already hidden
        return sheet;
    }

    [Fact]
    public void Nothing_Is_Soloed_Initially()
    {
        var m = new LayerVisibilityModel();
        Assert.Null(m.SoloedLayer);
        Assert.False(m.IsSoloed("A"));
    }

    [Fact]
    public void Solo_Shows_Only_That_Layer()
    {
        var sheet = ThreeLayers();
        var m = new LayerVisibilityModel();

        Assert.True(m.ToggleSolo(sheet, "B"));

        Assert.False(sheet.Layers[0].Visible);
        Assert.True(sheet.Layers[1].Visible);
        Assert.False(sheet.Layers[2].Visible);
        Assert.Equal("B", m.SoloedLayer);
    }

    [Fact]
    public void Toggling_Solo_Off_Restores_The_Previous_State()
    {
        var sheet = ThreeLayers();
        var m = new LayerVisibilityModel();

        m.ToggleSolo(sheet, "B");
        Assert.False(m.ToggleSolo(sheet, "B"));   // second click clears

        Assert.True(sheet.Layers[0].Visible);
        Assert.True(sheet.Layers[1].Visible);
        Assert.False(sheet.Layers[2].Visible);    // C was hidden before, still hidden
        Assert.Null(m.SoloedLayer);
    }

    [Fact]
    public void Switching_Solo_Preserves_The_Original_Snapshot()
    {
        var sheet = ThreeLayers();
        var m = new LayerVisibilityModel();

        m.ToggleSolo(sheet, "A");
        m.ToggleSolo(sheet, "C");   // switch, do not capture the soloed state
        m.Restore(sheet);

        // Must be the ORIGINAL state, not "only A visible".
        Assert.True(sheet.Layers[0].Visible);
        Assert.True(sheet.Layers[1].Visible);
        Assert.False(sheet.Layers[2].Visible);
    }

    [Fact]
    public void Restore_Without_Solo_Is_Harmless()
    {
        var sheet = ThreeLayers();
        var m = new LayerVisibilityModel();

        m.Restore(sheet);

        Assert.True(sheet.Layers[0].Visible);
        Assert.True(sheet.Layers[1].Visible);
        Assert.False(sheet.Layers[2].Visible);
    }

    [Fact]
    public void Reset_Drops_State_Without_Touching_Visibility()
    {
        var sheet = ThreeLayers();
        var m = new LayerVisibilityModel();

        m.ToggleSolo(sheet, "B");
        m.Reset();

        // Visibility stays as the solo left it; the stale snapshot is gone.
        Assert.False(sheet.Layers[0].Visible);
        Assert.True(sheet.Layers[1].Visible);
        Assert.Null(m.SoloedLayer);
    }

    [Fact]
    public void Soloing_A_Hidden_Layer_Makes_It_Visible()
    {
        var sheet = ThreeLayers();
        var m = new LayerVisibilityModel();

        m.ToggleSolo(sheet, "C");   // C started hidden

        Assert.True(sheet.Layers[2].Visible);
        Assert.False(sheet.Layers[0].Visible);
    }

    [Fact]
    public void IsSoloed_Tracks_The_Active_Layer()
    {
        var sheet = ThreeLayers();
        var m = new LayerVisibilityModel();

        m.ToggleSolo(sheet, "A");
        Assert.True(m.IsSoloed("A"));
        Assert.False(m.IsSoloed("B"));
    }
}
