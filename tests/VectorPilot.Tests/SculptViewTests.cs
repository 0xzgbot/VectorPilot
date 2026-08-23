using System.Windows;
using VectorPilot.App;
using VectorPilot.App.Controls;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// H-302: dragging on the 3D mesh changes the selected component's heightfield,
/// and the stroke is undoable. Tests drive the same public SculptAt seam the
/// XAML mouse handlers call — never SculptEngine in isolation.
/// </summary>
[Collection("STA")]
public class SculptViewTests
{
    private static void OnSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                if (Application.Current is null) _ = new Application();
                body();
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        Assert.True(t.Join(TimeSpan.FromSeconds(30)), "test hung");
        if (error is not null) throw error;
    }

    private static HeightfieldData FlatGrid(int w = 40, int h = 40, double v = 2.0)
    {
        var heights = new double[w * h];
        Array.Fill(heights, v);
        return new HeightfieldData(w, h, 1.0, 0, 0, heights);
    }

    [Fact]
    public void Drag_On_The_Mesh_Changes_The_Selected_Component_Heightfield()
    {
        OnSta(() =>
        {
            var panel = new ModelPanel();
            var vm = panel.Vm;
            vm.Add(FlatGrid(), "sculpt me");
            panel.Preview.ShowHeightfield(vm.Composite);

            var before = vm.Selected!.Heightfield.Heights.ToArray();

            // The drag path: viewport center → stock XY → Vm.Sculpt via the same
            // SculptStroke event the MouseLeftButtonDown/Move handlers raise.
            Assert.True(panel.Preview.SculptAt(new Point(200, 200)),
                "SculptAt refused — no field shown or no subscriber");

            var after = vm.Selected.Heightfield.Heights;
            bool changed = false;
            for (int i = 0; i < before.Length; i++)
                if (Math.Abs(before[i] - after[i]) > 1e-9) { changed = true; break; }

            Assert.True(changed, "drag produced zero heightfield change");
            Assert.True(vm.HasSculptUndo);
        });
    }

    [Fact]
    public void Undo_Restores_The_Pre_Stroke_Field()
    {
        OnSta(() =>
        {
            var panel = new ModelPanel();
            var vm = panel.Vm;
            vm.Add(FlatGrid(), "undo me");
            panel.Preview.ShowHeightfield(vm.Composite);

            var original = vm.Selected!.Heightfield.Heights.ToArray();
            Assert.True(panel.Preview.SculptAt(new Point(200, 200)));

            // The composite changed...
            Assert.NotEqual(original, vm.Selected.Heightfield.Heights);

            // ...and undo puts it back (same reference the snapshot kept).
            Assert.True(vm.UndoLastStroke());
            Assert.Equal(original, vm.Selected.Heightfield.Heights);
            Assert.False(vm.HasSculptUndo);

            // A second undo has nothing to restore.
            Assert.False(vm.UndoLastStroke());
        });
    }

    [Fact]
    public void Screen_Mapping_Follows_The_Shown_Field_Bounds()
    {
        OnSta(() =>
        {
            var preview = new ThreeDPreview();
            preview.ShowHeightfield(FlatGrid(w: 60, h: 20));

            // Viewport not rendered in tests → ActualWidth 0 → clamped divisor of 1.
            // Left edge maps to -w/2, right edge to +w/2.
            Assert.True(preview.TryScreenToStock(new Point(0, 0), out var x0, out var y0));
            Assert.Equal(-30, x0, 3);
            Assert.Equal(-10, y0, 3);

            // Any far-off-screen point clamps to the right/bottom edge.
            Assert.True(preview.TryScreenToStock(new Point(99999, 99999), out var x1, out var y1));
            Assert.Equal(+30, x1, 3);
            Assert.Equal(+10, y1, 3);

            // No field → refusal.
            preview.ShowHeightfield(null);
            Assert.False(preview.TryScreenToStock(new Point(0, 0), out _, out _));
            Assert.False(preview.SculptAt(new Point(0, 0)));
        });
    }

    [Fact]
    public void Sculpt_Outside_The_Component_Is_A_No_Op_And_Needs_No_Undo()
    {
        OnSta(() =>
        {
            // 1. No selection → the drag is refused outright. ModelPanel edits the
            // SHARED app stack, so empty it first — other tests may have left
            // components (and a selection) behind.
            var emptyPanel = new ModelPanel();
            emptyPanel.Vm.Components.Clear();
            emptyPanel.Vm.SelectedIndex = -1;
            emptyPanel.Preview.ShowHeightfield(FlatGrid());
            Assert.False(emptyPanel.Preview.SculptAt(new Point(200, 150)),
                "no selected component — the drag must be refused");

            // 2. A stroke whose brush reaches no cell must not arm undo.
            var vm = new ComponentTreeViewModel();
            vm.BrushRadiusMm = 0.05;   // smaller than half a cell (cell = 1mm)
            vm.Add(FlatGrid(), "edge case");
            bool applied = vm.Sculpt(SculptTool.Brush, -19.5, -19.5);
            Assert.False(applied, "a sub-cell brush must affect nothing");
            Assert.False(vm.HasSculptUndo, "an off-surface stroke must not arm undo");
        });
    }
}
