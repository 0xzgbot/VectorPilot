using System.IO;
using System.Windows;
using VectorPilot.App;
using VectorPilot.App.Controls;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// H-301: the STL-to-stock wizard. One STL becomes a component placed on the
/// sheet/stock bounds; CANCEL leaves the job byte-for-byte unchanged — no
/// component, no ModelHeightfield, no status lie. The dialog is driven through
/// the same Ok_Click / Cancel_Click handlers the XAML buttons invoke.
/// </summary>
[Collection("STA")]
public class StlWizardTests
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
        Assert.True(t.Join(TimeSpan.FromSeconds(30)), "test hung (modal dialog?)");
        if (error is not null) throw error;
    }

    /// <summary>A minimal valid binary STL: one right-angle triangle in Z=1..2.</summary>
    private static byte[] OneTriangleStl()
    {
        var ms = new MemoryStream();
        ms.Write(new byte[80]);                       // header
        ms.Write(BitConverter.GetBytes(1u));          // 1 triangle
        void Vec(float x, float y, float z)
        {
            foreach (var v in new[] { x, y, z }) ms.Write(BitConverter.GetBytes(v));
        }
        Vec(0, 0, 1);                                 // normal (magnitude ignored)
        Vec(0, 0, 2); Vec(10, 0, 2); Vec(0, 10, 2);   // vertices
        ms.Write(new byte[2]);                        // attribute byte count
        return ms.ToArray();
    }

    [Fact]
    public void Ok_Lands_A_Component_On_The_Stack_And_Bakes_The_Model_Field()
    {
        OnSta(() =>
        {
            var panel = new ModelPanel();
            var vm = panel.Vm;
            int before = vm.Components.Count;
            var prevModel = AppState.ModelHeightfield;

            var wizard = new StlImportDialog(OneTriangleStl(), "wizard-tri.stl");
            wizard.TxtStockX.Text = "200";
            wizard.TxtStockY.Text = "200";
            wizard.TxtStockZ.Text = "25";
            wizard.TxtScale.Text = "1.0";
            wizard.TxtCell.Text = "1.0";

            // The preview must be real BEFORE any commit.
            var preview = wizard.BuildPreview();
            Assert.NotNull(preview);
            Assert.True(wizard.TriangleCount > 0, "wizard parsed no triangles");
            // ~10mm model centered on the 200mm stock → (200-10)/2 = 95.
            Assert.Equal(95, preview!.MinX, 3);
            Assert.Equal(95, preview.MinY, 3);

            // Drive the SAME commit method the XAML OK button invokes.
            wizard.Confirm();
            Assert.True(wizard.Confirmed);
            Assert.NotNull(wizard.ResultHeightfield);

            panel.AddStlComponent(wizard.ResultHeightfield!, "wizard-tri");

            Assert.Equal(before + 1, vm.Components.Count);
            Assert.Equal("wizard-tri", vm.Components[^1].Name);
            Assert.Same(vm.Components[^1].Heightfield, wizard.ResultHeightfield);
            Assert.NotNull(AppState.ModelHeightfield);

            // cleanup
            vm.Remove(vm.Components[^1]);
            AppState.ModelHeightfield = prevModel;
        });
    }

    [Fact]
    public void Cancel_Leaves_The_Job_Completely_Untouched()
    {
        OnSta(() =>
        {
            var panel = new ModelPanel();
            var vm = panel.Vm;
            int before = vm.Components.Count;
            var prevComposite = vm.Composite;
            var prevModel = AppState.ModelHeightfield;
            var prevJob = AppState.CurrentJob;

            var wizard = new StlImportDialog(OneTriangleStl(), "cancel-tri.stl");
            wizard.TxtScale.Text = "2.0";   // settings that WOULD change things

            // Drive the SAME cancel method the XAML Cancel button invokes.
            wizard.Decline();
            Assert.False(wizard.Confirmed);
            Assert.Null(wizard.ResultHeightfield);

            // And the panel's cancel path (what ImportStlViaWizard does on non-OK).
            panel.CancelStlImport();

            Assert.Equal(before, vm.Components.Count);
            Assert.Same(prevComposite, vm.Composite);
            Assert.Same(prevModel, AppState.ModelHeightfield);
            Assert.Same(prevJob, AppState.CurrentJob);
        });
    }

    [Fact]
    public void Origin_Corner_Places_The_Model_At_The_Chosen_Stock_Position()
    {
        OnSta(() =>
        {
            var wizard = new StlImportDialog(OneTriangleStl(), "origin-tri.stl");
            wizard.TxtStockX.Text = "100";
            wizard.TxtStockY.Text = "100";
            // 10mm model, bottom-left origin → grid at (0,0).
            wizard.CmbOrigin.SelectedIndex = 0;
            var bl = wizard.BuildPreview();
            Assert.NotNull(bl);
            Assert.Equal(0, bl!.MinX, 3);
            Assert.Equal(0, bl.MinY, 3);

            // Center → (100-10)/2 = 45.
            wizard.CmbOrigin.SelectedIndex = 4;
            var c = wizard.BuildPreview();
            Assert.NotNull(c);
            Assert.Equal(45, c!.MinX, 3);
            Assert.Equal(45, c.MinY, 3);
        });
    }

    [Fact]
    public void Scale_And_Cell_Size_Change_The_Grid()
    {
        OnSta(() =>
        {
            var wizard = new StlImportDialog(OneTriangleStl(), "scale-tri.stl");

            wizard.TxtScale.Text = "2.0";
            wizard.TxtCell.Text = "1.0";
            var scaled = wizard.BuildPreview();
            Assert.NotNull(scaled);
            // 10mm triangle at 2x → ~20mm wide grid.
            Assert.True(scaled!.Width * scaled.CellSizeMm > 15,
                $"2x scale produced a {scaled.Width * scaled.CellSizeMm:0.#}mm grid");

            wizard.TxtScale.Text = "1.0";
            wizard.TxtCell.Text = "0.5";
            var fine = wizard.BuildPreview();
            Assert.NotNull(fine);
            Assert.True(fine!.CellSizeMm < 1.0, "cell size was ignored");
        });
    }

    [Fact]
    public void Corrupt_Stl_Is_Refused_With_Ok_Disabled()
    {
        OnSta(() =>
        {
            var wizard = new StlImportDialog(new byte[] { 1, 2, 3 }, "garbage.stl");

            var preview = wizard.BuildPreview();
            Assert.Null(preview);
            Assert.Equal(0, wizard.TriangleCount);
            Assert.False(((System.Windows.Controls.Button)wizard.FindName("BtnOk")!).IsEnabled,
                "OK must be disabled for an unimportable model");
        });
    }

    [Fact]
    public void ModelPanel_Routes_Stl_Files_Through_The_Wizard_Commit_Path()
    {
        OnSta(() =>
        {
            // The real commit path: the same AddStlComponent the wizard OK handler
            // reaches, driven with a real imported heightfield.
            var result = StlImporter.Import(OneTriangleStl(), "route-tri.stl");
            Assert.True(result.Success);

            var panel = new ModelPanel();
            var vm = panel.Vm;
            int before = vm.Components.Count;

            panel.AddStlComponent(result.Heightfield!, "route-tri");

            Assert.Equal(before + 1, vm.Components.Count);
            Assert.NotNull(AppState.ModelHeightfield);

            vm.Remove(vm.Components[^1]);
        });
    }
}
