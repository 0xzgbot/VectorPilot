using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using VectorPilot.Engine;

namespace VectorPilot.App.Controls;

/// <summary>
/// Model stage (card A6): hosts the component tree so it is reachable from the
/// rail, previews the live composite, and bakes it into the job for toolpathing.
/// </summary>
public partial class ModelPanel : UserControl
{
    private int _shapeCount;

    public ModelPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    private ComponentTreeViewModel Vm => Tree.Vm;

    private void Refresh()
    {
        bool any = Vm.Components.Count > 0;
        EmptyHint.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        BtnBake.IsEnabled = any && Vm.Composite is not null;

        if (Vm.Composite is { } hf)
        {
            Preview.ShowHeightfield(hf);
            StatusLabel.Text = $"composite {hf.Width}×{hf.Height} · max {hf.MaxHeight:F2} mm";
        }
        else
        {
            StatusLabel.Text = any ? "no visible components" : "";
        }
        Tree.Refresh();
    }

    private void ImportRelief_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Import relief",
            Filter = "Models and images (*.stl;*.obj;*.3mf;*.png;*.jpg;*.bmp)" +
                     "|*.stl;*.obj;*.3mf;*.png;*.jpg;*.bmp|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var hf = LoadHeightfield(dlg.FileName);
            if (hf is null) { StatusLabel.Text = "unsupported file"; return; }

            Vm.Add(hf, System.IO.Path.GetFileNameWithoutExtension(dlg.FileName));
            Refresh();
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"import failed: {ex.Message}";
        }
    }

    /// <summary>Dispatch a mesh file to its heightfield importer.</summary>
    private static HeightfieldData? LoadHeightfield(string path)
    {
        var bytes = System.IO.File.ReadAllBytes(path);
        var name = System.IO.Path.GetFileName(path);
        return System.IO.Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".stl" => StlImporter.Import(bytes, name).Heightfield,
            ".obj" => ObjImporter.Import(bytes).Heightfield,
            ".3mf" => ThreeMfImporter.Import(bytes).Heightfield,
            _ => null
        };
    }

    private void AddShape_Click(object sender, RoutedEventArgs e)
    {
        var sheet = AppState.CurrentJob.ActiveSheet;
        double sw = ParseDim(sheet.Width, 200), sh = ParseDim(sheet.Height, 200);
        double thick = ParseDim(sheet.Thickness, 18);
        double w = Math.Max(sw, 10), h = Math.Max(sh, 10);
        double cell = Math.Max(Math.Min(w, h) / 120.0, 0.25);

        var hf = ShapeReliefGenerator.Generate(
            ReliefShapeType.Round, null,
            width: w, height: h,
            cellSizeMm: cell, maxHeight: Math.Max(thick * 0.6, 3));

        Vm.Add(hf, $"Dome {++_shapeCount}");
        Refresh();
    }

    private static double ParseDim(object? value, double fallback)
        => value switch
        {
            double d => d,
            string s when double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var p) => p,
            _ => fallback
        };

    private void Bake_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.Composite is not { } hf) return;
        AppState.ModelHeightfield = hf;
        StatusLabel.Text = $"baked {hf.Width}×{hf.Height} → Toolpaths stage";
    }
}
