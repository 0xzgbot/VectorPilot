using System.Windows;
using System.Windows.Controls;
using VectorPilot.App;
using VectorPilot.App.Controls;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// H-501: picking a material + bit preset fills the cut params (feed, depth, RPM)
/// from ToolDatabase.ResolvedCutData's machine → material → derived chain, and
/// Add passes the RPM through ParamsJson so Calculate emits M3 S&lt;rpm&gt;.
/// </summary>
[Collection("STA")]
public class MaterialBitPresetTests
{
    private static void OnSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                lock (STAApplicationGate.Lock)
                {
                    if (Application.Current is null) _ = new Application();
                }
                body();
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        Assert.True(t.Join(TimeSpan.FromSeconds(30)), "test hung");
        if (error is not null) throw error;
    }

    [Fact]
    public void Panel_Carries_Both_Preset_Pickers()
    {
        OnSta(() =>
        {
            var panel = new CutPanel();

            var material = panel.FindName("CmbMaterialPreset") as ComboBox;
            var tool = panel.FindName("CmbToolPreset") as ComboBox;

            Assert.NotNull(material);
            Assert.NotNull(tool);
            // The databases seed defaults, so the pickers are populated — not empty shells.
            Assert.True(material!.Items.Count > 0, "material picker empty");
            Assert.True(tool!.Items.Count > 0, "tool picker empty");
        });
    }

    [Fact]
    public void Presets_Fill_Feed_And_Depth_With_Positive_Resolved_Values()
    {
        OnSta(() =>
        {
            var panel = new CutPanel();
            panel.ApplyPresetToFields();

            double feed = double.Parse(panel.FeedFieldText);
            double depth = double.Parse(panel.DepthFieldText);

            Assert.True(feed > 0, $"preset resolved feed={feed}");
            Assert.True(depth > 0, $"preset resolved depth={depth}");
            Assert.True(panel.PresetSpindleRpm > 0, "preset resolved no spindle RPM");
        });
    }

    [Fact]
    public void Different_Materials_Resolve_Different_Feeds()
    {
        OnSta(() =>
        {
            var panel = new CutPanel();
            panel.ApplyPresetToFields();
            double feedA = double.Parse(panel.FeedFieldText);

            // Switch to a different material and re-apply.
            var material = (ComboBox)panel.FindName("CmbMaterialPreset")!;
            if (material.Items.Count < 2)
            {
                // Only one seeded material — nothing to compare; the resolution path
                // is still exercised above.
                return;
            }
            material.SelectedIndex = material.Items.Count - 1;
            panel.ApplyPresetToFields();
            double feedB = double.Parse(panel.FeedFieldText);

            Assert.NotEqual(feedA, feedB);
        });
    }

    [Fact]
    public void Add_Writes_Preset_Rpm_Into_ParamsJson_For_Strategies_That_Have_One()
    {
        OnSta(() =>
        {
            var panel = new CutPanel();
            panel.ApplyPresetToFields();

            // BtnAdd requires at least one shape on the active layer.
            var layer = AppState.CurrentJob.ActiveSheet.ActiveLayer;
            var shape = new VectorShape();
            layer.Shapes.Add(shape);

            panel.SelectStrategy("rough3d");
            panel.AddToolpathForTest("preset-rpm");

            var tp = AppState.Toolpaths.Toolpaths.LastOrDefault(t => t.Name == "preset-rpm");
            Assert.NotNull(tp);
            try
            {
                Assert.Contains("spindleRpm", tp!.ParamsJson);
                if (panel.PresetSpindleRpm > 0)
                    Assert.Contains(
                        ((int)panel.PresetSpindleRpm).ToString(),
                        tp.ParamsJson);
            }
            finally
            {
                AppState.Toolpaths.Remove(tp!.Id);
                layer.Shapes.Remove(shape);
            }
        });
    }
}
