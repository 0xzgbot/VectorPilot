using System.IO;
using System.Windows;
using System.Windows.Controls;
using VectorPilot.Engine;

namespace VectorPilot.App.Controls;

/// <summary>
/// H-301: MeshCAM-style STL-to-stock wizard. The user picks stock bounds (or adopts
/// the job sheet), an origin corner, a scale and a cell size; the dialog previews the
/// model's extents against the stock BEFORE anything touches the job. OK returns the
/// finished heightfield via <see cref="ResultHeightfield"/>; Cancel (Escape, X, or the
/// button) leaves every job structure untouched — the model is only imported on OK.
/// </summary>
public partial class StlImportDialog : Window
{
    private readonly byte[] _stlBytes;
    private readonly string _sourceName;

    /// <summary>The imported heightfield after a confirmed OK; null until then.</summary>
    public HeightfieldData? ResultHeightfield { get; private set; }

    /// <summary>Triangle count of the parsed model, for tests.</summary>
    public int TriangleCount { get; private set; }

    public StlImportDialog(string stlPath)
        : this(File.ReadAllBytes(stlPath), Path.GetFileName(stlPath))
    {
    }

    public StlImportDialog(byte[] stlBytes, string sourceName = "model.stl")
    {
        InitializeComponent();
        _stlBytes = stlBytes;
        _sourceName = sourceName;
        FileLabel.Text = sourceName;
        Loaded += (_, _) => RefreshPreview();

        // Adopt the job sheet by default when one exists — MeshCAM's most common path.
        if (AppState.CurrentJob?.ActiveSheet is not null)
        {
            ChkFitSheet.IsChecked = true;   // handler fills X/Y from the sheet
        }
    }

    // ---- field plumbing ----

    private double StockX => ParseNum(TxtStockX.Text, 200);
    private double StockY => ParseNum(TxtStockY.Text, 200);
    private double StockZ => ParseNum(TxtStockZ.Text, 25);
    private double Scale => Math.Max(0.0001, ParseNum(TxtScale.Text, 1.0));
    private double CellSize => Math.Max(0.05, ParseNum(TxtCell.Text, 1.0));

    private static double ParseNum(string text, double fallback)
        => double.TryParse(text.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 ? v : fallback;

    private void Field_Changed(object sender, TextChangedEventArgs e) => RefreshPreview();

    private void FitSheet_Changed(object sender, RoutedEventArgs e)
    {
        if (ChkFitSheet.IsChecked == true)
        {
            var sheet = AppState.CurrentJob?.ActiveSheet;
            if (sheet is not null)
            {
                TxtStockX.Text = ParseDim(sheet.Width, 200).ToString(System.Globalization.CultureInfo.InvariantCulture);
                TxtStockY.Text = ParseDim(sheet.Height, 200).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        RefreshPreview();
    }

    private static double ParseDim(object? value, double fallback)
        => value switch
        {
            double d => d,
            string s when double.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var p) => p,
            _ => fallback
        };

    /// <summary>Origin corner → MinX/MinY offset for the heightfield grid.</summary>
    internal (double MinX, double MinY) OriginOffset(double modelW, double modelH)
    {
        return CmbOrigin.SelectedIndex switch
        {
            0 => (0, 0),                                        // bottom-left
            1 => ((StockX - modelW) / 2, 0),                    // bottom-center
            2 => (StockX - modelW, 0),                          // bottom-right
            3 => (0, StockY - modelH),                          // top-left
            _ => ((StockX - modelW) / 2, (StockY - modelH) / 2) // center
        };
    }

    /// <summary>Parse + rasterize the STL with the CURRENT settings. No job mutation.</summary>
    public HeightfieldData? BuildPreview()
    {
        var parsed = StlImporter.Parse(_stlBytes);
        TriangleCount = parsed.Count;
        if (parsed.Count == 0) return null;

        var result = StlImporter.Import(_stlBytes, _sourceName, CellSize, Scale);
        if (!result.Success || result.Heightfield is not { } hf) return null;

        // Place the grid at the chosen origin corner within the stock.
        double w = hf.Width * hf.CellSizeMm, h = hf.Height * hf.CellSizeMm;
        var (minX, minY) = OriginOffset(w, h);
        if (Math.Abs(hf.MinX - minX) < 1e-9 && Math.Abs(hf.MinY - minY) < 1e-9) return hf;

        return new HeightfieldData(hf.Width, hf.Height, hf.CellSizeMm, minX, minY, hf.Heights);
    }

    private void RefreshPreview()
    {
        if (PreviewLabel is null) return;   // XAML not fully wired during ctor

        var hf = TryBuildQuietly();
        if (hf is null)
        {
            PreviewLabel.Text = TriangleCount == 0
                ? $"{_sourceName}: no valid triangles — cannot import"
                : "invalid settings";
            BtnOk.IsEnabled = false;
            FitNote.Text = "";
            FitBar.Value = 0;
            return;
        }

        double modelW = hf.Width * hf.CellSizeMm, modelH = hf.Height * hf.CellSizeMm;
        bool fitsXy = modelW <= StockX + 1e-9 && modelH <= StockY + 1e-9;
        bool fitsZ = hf.MaxHeight <= StockZ + 1e-9;

        PreviewLabel.Text = $"preview: {hf.Width}×{hf.Height} cells · model {modelW:0.#}×{modelH:0.#}mm · " +
                            $"top {hf.MaxHeight:0.##}mm · {TriangleCount} triangles";
        FitBar.Value = Math.Min(1.0, Math.Max(
            modelW / Math.Max(StockX, 1e-9), modelH / Math.Max(StockY, 1e-9)));
        FitNote.Text = (fitsXy, fitsZ) switch
        {
            (true, true) => "fits inside the stock",
            (true, false) => $"⚠ model top ({hf.MaxHeight:0.##}mm) exceeds stock Z ({StockZ:0.#}mm)",
            (false, true) => "⚠ model wider than the stock in X or Y",
            (false, false) => "⚠ model exceeds the stock in X/Y and Z"
        };
        FitNote.Foreground = fitsXy && fitsZ
            ? System.Windows.Media.Brushes.DarkGreen
            : System.Windows.Media.Brushes.Firebrick;

        // A model that overhangs the stock can still be added (the user may plan to
        // rescale), so OK stays enabled — but the warning is unmissable.
        BtnOk.IsEnabled = true;
    }

    private HeightfieldData? TryBuildQuietly()
    {
        try { return BuildPreview(); }
        catch { return null; }
    }

    // ---- commit / cancel ----

    /// <summary>Confirm the import. The OK button's handler routes here; public so
    /// tests drive the exact commit path without showing a modal.</summary>
    public void Confirm()
    {
        var hf = TryBuildQuietly();
        if (hf is null)
        {
            MessageBox.Show(this, "The STL could not be imported with these settings.",
                "VectorPilot", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ResultHeightfield = hf;   // ONLY assignment point — Cancel never reaches it
        Confirmed = true;
        if (IsLoaded) DialogResult = true;   // only valid once shown as a dialog
    }

    /// <summary>Cancel the import. The Cancel button's handler routes here; public so
    /// tests drive the exact cancel path without showing a modal.</summary>
    public void Decline()
    {
        ResultHeightfield = null;   // explicit: no half-built state escapes the dialog
        Confirmed = false;
        if (IsLoaded) DialogResult = false;
    }

    /// <summary>Whether the user confirmed (true) or cancelled (false). Read this when
    /// the window was never shown as a dialog — <see cref="Window.DialogResult"/> is
    /// only settable on a shown window.</summary>
    public bool Confirmed { get; private set; }

    private void Ok_Click(object sender, RoutedEventArgs e) => Confirm();

    private void Cancel_Click(object sender, RoutedEventArgs e) => Decline();
}
