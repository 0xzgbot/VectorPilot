using System.Windows;
using System.Windows.Controls;
using VectorPilot.App;
using VectorPilot.App.Controls;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// H-302: dragging on the 3D mesh changes the selected component's heightfield,
/// and the stroke is undoable. Tests drive the same public SculptAt seam the
/// XAML mouse handlers call — never SculptEngine in isolation.
/// H-303 additions live here too: the split 2D | 3D shell stage and the
/// component tree's height/fade controls (this file's STA harness is the one
/// new WPF test classes must not race — see the suite's shared-Application
/// ordering).
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
                var res = Application.Current!.Resources;
                if (!res.Contains("PanelBg"))
                {
                    res["RailBg"] = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x19, 0x19, 0x22));
                    res["Accent"] = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x3D, 0x7E, 0xFF));
                    res["PanelBg"] = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xF4, 0xF4, 0xF6));
                    res["RailButton"] = new Style(typeof(Button));
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

    // ---- H-303: split 2D | 3D + component height/fade ----

    private static void ClickStage(MainWindow w, string tag)
        => typeof(MainWindow).GetMethod("Stage_ClickByTag",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance)!.Invoke(w, new object?[] { tag });

    [Fact]
    public void Split_Toggle_Shows_Both_Stages_And_Toggles_Back()
    {
        OnSta(() =>
        {
            var w = new MainWindow();

            // ON: the stage host holds a Grid with BOTH panels inside.
            Assert.True(w.ToggleSplitView());
            Assert.True(w.IsSplitViewActive);

            var host = (ContentControl)w.FindName("StageHost")!;
            var grid = Assert.IsType<Grid>(host.Content);

            var panels = new List<UIElement>();
            Collect(grid, panels);
            Assert.Contains(panels, p => p is DesignPanel);
            Assert.Contains(panels, p => p is ModelPanel);

            // OFF: back to the Model stage alone.
            Assert.False(w.ToggleSplitView());
            Assert.False(w.IsSplitViewActive);
            Assert.IsType<ModelPanel>(host.Content);
        });
    }

    [Fact]
    public void Entering_Design_Or_Model_Reenters_An_Active_Split_Other_Stages_Leave_It()
    {
        OnSta(() =>
        {
            var w = new MainWindow();
            Assert.True(w.ToggleSplitView());

            var host = (ContentControl)w.FindName("StageHost")!;

            // The rail's Model button while split: still both stages (the split IS
            // where both of them live).
            ClickStage(w, "model");
            Assert.True(w.IsSplitViewActive);
            Assert.IsType<Grid>(host.Content);

            // Any other stage leaves the split behind.
            ClickStage(w, "photo");
            Assert.False(w.IsSplitViewActive);
            Assert.IsType<PhotoPanel>(host.Content);
        });
    }

    [Fact]
    public void Height_Scale_On_A_Component_Changes_The_Composite_Max_Height()
    {
        OnSta(() =>
        {
            var panel = new ModelPanel();
            var vm = panel.Vm;
            vm.Components.Clear();   // shared app stack — start clean
            var c = vm.Add(FlatGrid(), "scale me");

            double beforeMax = vm.Composite!.MaxHeight;
            var beforeGrid = vm.Composite.Heights.ToArray();

            vm.Selected!.HeightScale = 2.0;
            vm.Recomposite();

            Assert.Equal(beforeMax * 2.0, vm.Composite.MaxHeight, 5);
            Assert.NotEqual(beforeGrid, vm.Composite.Heights);

            // Leave the shared stack as found — a stray misaligned grid turns every
            // later composite (e.g. the STL wizard's) into null.
            vm.Remove(c);
        });
    }

    [Fact]
    public void Fade_Ramp_Lowers_One_Edge_Of_The_Composite_But_Not_The_Other()
    {
        OnSta(() =>
        {
            var panel = new ModelPanel();
            var vm = panel.Vm;
            vm.Components.Clear();   // shared app stack — start clean
            var c = vm.Add(FlatGrid(), "fade me");

            vm.Selected!.FadeAmount = 0.75;
            vm.Selected.FadeDirection = FadeDirection.LeftToRight;
            vm.Recomposite();

            var hf = vm.Composite!;
            Assert.Equal(2.0, hf.Heights[0], 3);                   // left (full) edge untouched
            Assert.Equal(2.0 * 0.25, hf.Heights[hf.Width - 1], 3); // right edge scaled to 1-amount

            vm.Remove(c);
        });
    }

    [Fact]
    public void The_Tree_Panel_Height_Control_Recomposites_And_Fires_The_Redraw_Event()
    {
        OnSta(() =>
        {
            var panel = new ModelPanel();
            var tree = (ComponentTreePanel)panel.FindName("Tree")!;
            var vm = panel.Vm;
            vm.Components.Clear();   // shared app stack — start clean
            var c = vm.Add(FlatGrid(), "slider me");

            // The modifier controls are dead until the panel's Loaded hook runs
            // (_ready) — raise the real event rather than poking the flag.
            tree.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            bool redrawFired = false;
            vm.CompositeChanged += () => redrawFired = true;

            // Drive the exact code path the Height slider runs; the composite must
            // change through ComponentModifierEngine, not a direct grid write.
            vm.SelectedIndex = 0;
            tree.RaiseModifierChangedForTest(heightScale: 1.5, fadeAmount: null, fadeDirection: null);

            Assert.True(redrawFired, "composite change did not announce itself");
            Assert.Equal(3.0, vm.Composite!.MaxHeight, 5);

            vm.Remove(c);
        });
    }

    private static void Collect(DependencyObject parent, List<UIElement> into)
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < n; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is UIElement u) into.Add(u);
            Collect(child, into);
        }
    }
}
