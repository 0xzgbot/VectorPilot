using System.Windows;
using VectorPilot.App;
using VectorPilot.App.Controls;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// P-102: the Type tool places text on the Design canvas as vector outlines via
/// click-to-place, reusing TextToCurves (no second glyph pipeline). Empty text
/// places nothing; placement is undoable.
/// </summary>
[Collection("STA")]
public class TypeToolTests
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
    public void Type_Tool_Radio_Exists_And_Selects_Type_Mode()
    {
        OnSta(() =>
        {
            var panel = new DesignPanel();
            var radio = (System.Windows.Controls.RadioButton)panel.FindName("ToolType")!;
            Assert.NotNull(radio);

            radio.IsChecked = true;
            Assert.Equal(DesignPanel.Tool.Type, panel.CurrentTool);
        });
    }

    [Fact]
    public void Placing_Text_Lands_Vector_Outlines_At_The_Click_Point()
    {
        OnSta(() =>
        {
            var job = Engine.Job.CreateDefault();
            AppState.RestoreJob(job);
            var layer = AppState.CurrentJob.ActiveSheet.ActiveLayer;
            int before = layer.Shapes.Count;

            var panel = new DesignPanel();
            int added = panel.PlaceTypeAt(new VectorPoint(50, 40), textOverride: "AB", sizeMmOverride: 20);

            Assert.True(added > 0, "placing 'AB' produced no outline shapes");
            Assert.Equal(before + added, layer.Shapes.Count);

            // All new geometry sits at/after the click point (baseline origin there,
            // glyphs extend right and up).
            var fresh = layer.Shapes.Skip(before).ToList();
            double minX = fresh.Min(s => s.Points.Min(p => p.X));
            double minY = fresh.Min(s => s.Points.Min(p => p.Y));
            Assert.InRange(minX, 49.0, 52.0);   // starts essentially AT the click X
            Assert.True(minY >= 39.5, $"glyphs dropped below the baseline: {minY}");

            // Undo removes them.
            Assert.True(panel.Undo.CanUndo);
            panel.Undo.Undo();
            Assert.Equal(before, layer.Shapes.Count);
        });
    }

    [Fact]
    public void Empty_Text_Places_Nothing()
    {
        OnSta(() =>
        {
            var job = Engine.Job.CreateDefault();
            AppState.RestoreJob(job);
            var layer = AppState.CurrentJob.ActiveSheet.ActiveLayer;
            int before = layer.Shapes.Count;

            var panel = new DesignPanel();
            Assert.Equal(0, panel.PlaceTypeAt(new VectorPoint(10, 10), textOverride: ""));
            Assert.Equal(0, panel.PlaceTypeAt(new VectorPoint(10, 10), textOverride: "   "));
            Assert.Equal(before, layer.Shapes.Count);
        });
    }

    [Fact]
    public void Size_Scales_The_Glyph_Height()
    {
        OnSta(() =>
        {
            var job = Engine.Job.CreateDefault();
            AppState.RestoreJob(job);
            var layer = AppState.CurrentJob.ActiveSheet.ActiveLayer;

            var panel = new DesignPanel();

            // Place "I" (a tall narrow glyph) at two sizes, measure heights.
            panel.PlaceTypeAt(new VectorPoint(0, 0), textOverride: "I", sizeMmOverride: 10);
            var first = layer.Shapes[^1];
            double hSmall = first.Points.Max(p => p.Y) - first.Points.Min(p => p.Y);

            int mid = layer.Shapes.Count;
            panel.PlaceTypeAt(new VectorPoint(200, 0), textOverride: "I", sizeMmOverride: 30);
            var second = layer.Shapes[mid];
            double hLarge = second.Points.Max(p => p.Y) - second.Points.Min(p => p.Y);

            Assert.True(hLarge > hSmall * 2, $"{hLarge} vs {hSmall} — size not scaling");
            // Inked height is normalized to the requested mm exactly.
            Assert.Equal(10.0, hSmall, 2);
            Assert.Equal(30.0, hLarge, 2);
        });
    }
}
