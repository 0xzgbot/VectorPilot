using System.Windows;
using VectorPilot.App;
using VectorPilot.App.Controls;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// P-302: the material chosen in Setup is the same preset the Cut stage resolves —
/// creating an Oak job puts the mapped database entry (Hardwood) in Cut's material
/// combo, so first-run feeds match the job you just made instead of falling back to
/// the database's first entry.
///
/// AppState is process-wide shared state; tests that swap the job snapshot and
/// restore it so parallel non-STA suites (FullFlowE2E) never observe a cleared
/// toolpath tree.
/// </summary>
[Collection("STA")]
public class JobMaterialPresetTests : IDisposable
{
    private Engine.Job? _savedJob;
    private List<Engine.Toolpath> _savedToolpaths = new();

    private void SwapJob(string materialName)
    {
        // RestoreJob does NOT clear the shared toolpath tree (only NewJob does),
        // so parallel non-STA suites never observe a cleared list mid-run.
        _savedJob = AppState.CurrentJob;
        _savedToolpaths = AppState.Toolpaths.Toolpaths.ToList();

        var job = Engine.Job.CreateDefault();
        job.Name = "preset-test";
        job.Sheets[0].Width = 300;
        job.Sheets[0].Height = 200;
        job.Sheets[0].Thickness = 18;
        job.Sheets[0].Units = Engine.UnitSystem.Millimeters;
        job.Sheets[0].Material = new Engine.Material { Name = materialName };
        AppState.RestoreJob(job);
    }

    public void Dispose()
    {
        if (_savedJob is not null)
        {
            AppState.RestoreJob(_savedJob);
            foreach (var t in _savedToolpaths) AppState.Toolpaths.Toolpaths.Add(t);
        }
    }

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
    public void Job_Material_Is_Selected_In_The_Cut_Preset_Combo()
    {
        OnSta(() =>
        {
            // Setup "Oak" maps onto the database catalog name "Hardwood" (documented
            // mapping — the card explicitly allows this).
            SwapJob("Oak");

            var panel = new CutPanel();   // PopulatePresets runs in the ctor

            var combo = (System.Windows.Controls.ComboBox)panel.FindName("CmbMaterialPreset")!;
            Assert.True(combo.Items.Count > 0);
            Assert.Equal("Hardwood", combo.SelectedItem as string);
        });
    }

    [Fact]
    public void Changing_Setup_Material_Changes_The_Cut_Selection()
    {
        OnSta(() =>
        {
            SwapJob("MDF");
            var panel = new CutPanel();
            var combo = (System.Windows.Controls.ComboBox)panel.FindName("CmbMaterialPreset")!;
            Assert.Equal("MDF", combo.SelectedItem as string);

            // Operator returns to Setup and picks Acrylic.
            SwapJob("Acrylic");
            panel.PopulatePresetsForTest();
            Assert.Equal("Acrylic", combo.SelectedItem as string);
        });
    }

    [Fact]
    public void Unknown_Job_Material_Falls_Back_To_The_First_Entry()
    {
        OnSta(() =>
        {
            SwapJob("Unobtainium");
            var panel = new CutPanel();

            var combo = (System.Windows.Controls.ComboBox)panel.FindName("CmbMaterialPreset")!;
            // Falls back to index 0 rather than a blank selection.
            Assert.Equal(0, combo.SelectedIndex);
        });
    }
}
