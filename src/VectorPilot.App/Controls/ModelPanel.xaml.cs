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
    private int _weaveCount;

    /// <summary>The component stack this panel edits (shared app stack). Public because
    /// the test project has no InternalsVisibleTo.</summary>
    public ComponentTreeViewModel Vm => Tree.Vm;

    /// <summary>The 3D preview (public: tests drive the sculpt drag seam).</summary>
    public ThreeDPreview Preview => _preview;

    private ThreeDPreview _preview = null!;   // assigned in InitializeComponent

    public ModelPanel()
    {
        InitializeComponent();
        _preview = (ThreeDPreview)FindName("View3D")!;   // expose the internal x:Name field publicly
        // H-211: one shared component stack for the whole app — a grayscale photo
        // relief lands in the same stack this panel's tree displays.
        Tree.UseSharedStack(AppState.Components);
        // H-302: drag on the 3D mesh strokes the selected component's heightfield,
        // then the composite re-renders — the sculpt loop.
        Preview.SculptStroke += (x, y) =>
        {
            if (!Vm.Sculpt(SculptTool.Brush, x, y)) return false;
            if (Vm.Composite is { } hf) _preview.ShowHeightfield(hf);
            BtnSculptUndo.IsEnabled = true;
            return true;
        };
        // H-303: height/fade edits recomposite without a click — redraw on every change.
        // AppState.Components outlives any one panel (tests construct many), so a stale
        // panel must not touch the visual tree from another thread: skip instead.
        Vm.CompositeChanged += () =>
        {
            if (!Dispatcher.CheckAccess()) return;
            if (Vm.Composite is { } hf) _preview.ShowHeightfield(hf);
        };
        Loaded += (_, _) =>
        {
            CmbWeavePattern.ItemsSource = new[] { "Plain", "Twill", "Satin" };
            CmbWeavePattern.SelectedIndex = 0;
            Refresh();
        };
    }

    /// <summary>H-302: undo the last sculpt stroke and re-render.</summary>
    private void SculptUndo_Click(object sender, RoutedEventArgs e)
    {
        if (!Vm.UndoLastStroke()) return;
        if (Vm.Composite is { } hf) Preview.ShowHeightfield(hf);
        BtnSculptUndo.IsEnabled = Vm.HasSculptUndo;
    }

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

        // H-301: an STL goes through the stock-setup wizard (stock/origin/scale/preview);
        // every other format keeps the direct one-shot import.
        if (dlg.FileName.EndsWith(".stl", StringComparison.OrdinalIgnoreCase))
        {
            ImportStlViaWizard(dlg.FileName);
            return;
        }

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

    /// <summary>
    /// H-301: run the STL through the stock wizard. Nothing touches the job until the
    /// user confirms OK — cancel leaves components and AppState exactly as they were.
    /// </summary>
    internal void ImportStlViaWizard(string stlPath)
    {
        var wizard = new StlImportDialog(stlPath) { Owner = Window.GetWindow(this) };
        wizard.ShowDialog();
        if (!wizard.Confirmed || wizard.ResultHeightfield is not { } hf)
        {
            CancelStlImport();
            return;
        }

        AddStlComponent(hf, System.IO.Path.GetFileNameWithoutExtension(stlPath),
            $"{wizard.TriangleCount} triangles");
    }

    /// <summary>The commit half of the wizard flow (public seam: tests drive it with
    /// the exact heightfield an OK'd dialog produced).</summary>
    public void AddStlComponent(HeightfieldData hf, string name, string? note = null)
    {
        Vm.Add(hf, name);
        AppState.ModelHeightfield = Vm.Composite;
        Refresh();
        StatusLabel.Text = note is null
            ? $"STL component added: {name}"
            : $"STL component added ({note})";
    }

    /// <summary>The cancel half of the wizard flow: status only, zero job mutation.</summary>
    public void CancelStlImport()
    {
        StatusLabel.Text = "STL import cancelled — job unchanged";
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

    // ---- model offset (ModelOffsetEngine had zero app call-sites) ----

    /// <summary>The relief as it was before the last offset, for one-step undo.</summary>
    private HeightfieldData? _preOffsetHeightfield;

    private void ModelOffset_Click(object sender, RoutedEventArgs e) => DoModelOffset();

    /// <summary>
    /// Inflate or deflate the composite relief. ModelOffsetEngine existed with no app
    /// call-site, so a user could never thicken a model before roughing.
    /// </summary>
    internal bool DoModelOffset(double? offsetOverride = null)
    {
        var hf = AppState.Heightfield ?? AppState.ModelHeightfield;
        if (hf is null)
        {
            StatusLabel.Text = ("No relief loaded — import a model or bitmap first");
            return false;
        }

        double mm = offsetOverride ?? (double.TryParse(TxtModelOffset.Text, out var v) ? v : 0);
        if (Math.Abs(mm) < 1e-9)
        {
            StatusLabel.Text = ("Offset of 0mm changes nothing");
            return false;
        }

        var result = ModelOffsetEngine.Offset(hf, new ModelOffsetEngine.OffsetParams { OffsetMm = mm });
        if (result is null)
        {
            StatusLabel.Text = ("Offset produced no change");
            return false;
        }

        _preOffsetHeightfield = hf;
        AppState.Heightfield = result.Heightfield;
        AppState.ModelHeightfield = result.Heightfield;
        BtnModelOffsetUndo.IsEnabled = true;

        StatusLabel.Text = ($"Offset {mm:+0.##;-0.##}mm — {result.ChangedCellCount} cells, " +
                  $"max height now {result.MaxHeightAfter:0.##}mm");
        Refresh();
        return true;
    }

    private void ModelOffsetUndo_Click(object sender, RoutedEventArgs e) => UndoModelOffset();

    /// <summary>Restore the relief captured before the last offset.</summary>
    internal bool UndoModelOffset()
    {
        if (_preOffsetHeightfield is null)
        {
            StatusLabel.Text = ("Nothing to undo");
            return false;
        }

        AppState.Heightfield = _preOffsetHeightfield;
        AppState.ModelHeightfield = _preOffsetHeightfield;
        _preOffsetHeightfield = null;
        BtnModelOffsetUndo.IsEnabled = false;

        StatusLabel.Text = ("Offset undone");
        Refresh();
        return true;
    }

    // ---- animated camera handlers (animated camera) ----

    private void ViewIso_Click(object sender, RoutedEventArgs e)
        => Preview.AnimateToView(CameraViewpoint.Isometric);

    private void ViewTop_Click(object sender, RoutedEventArgs e)
        => Preview.AnimateToView(CameraViewpoint.Top);

    private void ViewFront_Click(object sender, RoutedEventArgs e)
        => Preview.AnimateToView(CameraViewpoint.Front);

    private void ViewRight_Click(object sender, RoutedEventArgs e)
        => Preview.AnimateToView(CameraViewpoint.Right);

    private void Orbit_Click(object sender, RoutedEventArgs e)
        => BtnOrbit.Content = Preview.ToggleOrbit() ? "■ Stop" : "▶ Orbit";

    /// <summary>Wire the weave generator to the UI (it shipped engine-only).</summary>
    private void AddWeave_Click(object sender, RoutedEventArgs e)
    {
        var sheet = AppState.CurrentJob.ActiveSheet;
        double w = Math.Max(ParseDim(sheet.Width, 200), 10);
        double h = Math.Max(ParseDim(sheet.Height, 200), 10);
        double thick = ParseDim(sheet.Thickness, 18);

        var pattern = (CmbWeavePattern.SelectedItem as string) switch
        {
            "Twill" => WeavePattern.Twill,
            "Satin" => WeavePattern.Satin,
            _ => WeavePattern.Plain
        };

        var hf = WeaveReliefGenerator.Generate(
            new WeaveParams
            {
                Pattern = pattern,
                WarpCount = 12,
                WeftCount = 12,
                ThreadSize = Math.Min(w, h) / 14.0,
                Overlap = 0.5
            },
            width: w, height: h,
            cellSizeMm: Math.Max(Math.Min(w, h) / 200.0, 0.25),
            threadHeight: Math.Max(thick * 0.25, 2));

        Vm.Add(hf, $"{pattern} weave {++_weaveCount}");
        Refresh();
    }

    private void Bake_Click(object sender, RoutedEventArgs e)
    {
        if (Vm.Composite is not { } hf) return;
        AppState.ModelHeightfield = hf;
        StatusLabel.Text = $"baked {hf.Width}×{hf.Height} → Toolpaths stage";
    }
}
