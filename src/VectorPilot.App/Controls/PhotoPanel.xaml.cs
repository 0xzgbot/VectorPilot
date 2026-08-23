using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VectorPilot.Engine;
using VectorPilot.Geometry;

namespace VectorPilot.App.Controls;

/// <summary>
/// H-210: the photo workspace. Import a PNG/JPG, adjust contrast/brightness/invert/crop,
/// SEE the resulting heightfield live, then send it to one of three real operations —
/// Photo V-carve, Lithophane, or 3D relief — each of which lands in the Cuts list and
/// emits G-code through StrategyRegistry.
///
/// The preview is the honesty device: if it were an empty box or a flat grey rectangle the
/// user could not tell their photo had actually been read. It renders the exact luminance
/// grid that Compute will consume.
///
/// Empty image → honest refusal via the registry's own Empty() path (the strategy entries
/// already refuse when AppState.Heightfield is null); this panel never fakes G-code.
/// </summary>
public partial class PhotoPanel : UserControl
{
    private double[]? _luminance;     // 0..1 per pixel
    private int _width, _height;
    private string _fileName = "photo";

    /// <summary>
    /// H-211: raised whenever a photo action lands a toolpath in AppState.Toolpaths.
    /// MainWindow subscribes this to CutPanel.RefreshCutsList so the Cuts list — a
    /// snapshot rebuilt on demand — actually shows the new row without navigating.
    /// </summary>
    public event Action? CutsChanged;

    /// <summary>H-211 test seam: fire the refresh signal exactly as BuildToolpath does.</summary>
    public void RaiseCutsChangedForTest() => CutsChanged?.Invoke();

    public PhotoPanel()
    {
        InitializeComponent();
    }

    // ---- import + adjustments ----

    private void ImportPhoto_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import photo",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            LoadImage(dlg.FileName);
        }
        catch (Exception ex)
        {
            PhotoStatus.Text = $"import failed: {ex.Message}";
        }
    }

    /// <summary>Decode any WPF-supported image into our luminance grid (max 512px side).</summary>
    public void LoadImage(string path)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(System.IO.Path.GetFullPath(path));
        bmp.EndInit();
        bmp.Freeze();

        int maxSide = 512;
        double scale = Math.Min(1.0, maxSide / (double)Math.Max(bmp.PixelWidth, bmp.PixelHeight));
        int w = Math.Max(1, (int)(bmp.PixelWidth * scale));
        int h = Math.Max(1, (int)(bmp.PixelHeight * scale));

        var converted = new FormatConvertedBitmap(bmp, PixelFormats.Gray8, null, 0);
        var resized = new TransformedBitmap(converted, new ScaleTransform(
            w / (double)bmp.PixelWidth, h / (double)bmp.PixelHeight));
        resized.Freeze();

        int stride = (w + 3) & ~3;
        var pixels = new byte[stride * h];
        resized.CopyPixels(pixels, stride, 0);

        _width = w; _height = h;
        _luminance = new double[w * h];
        for (int i = 0; i < w * h; i++)
            _luminance[i] = pixels[i] / 255.0;

        _fileName = System.IO.Path.GetFileNameWithoutExtension(path);
        PhotoStatus.Text = $"{_fileName}: {w}x{h}px";
        AdjustPanel.Visibility = Visibility.Visible;
        foreach (var b in new[] { BtnPhotoVCarve, BtnLithophane, BtnRelief3D }) b.IsEnabled = true;

        RebuildPreview();
    }

    private void Adjust_Changed(object sender, RoutedEventArgs e) => RebuildPreview();

    private void Crop_Click(object sender, RoutedEventArgs e)
    {
        if (_luminance is null) return;
        int x0 = _width / 4, y0 = _height / 4;
        int w = _width / 2, h = _height / 2;
        var cropped = new double[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                cropped[y * w + x] = _luminance[(y + y0) * _width + (x + x0)];

        _luminance = cropped; _width = w; _height = h;
        PhotoStatus.Text = $"{_fileName}: cropped to {_width}x{_height}";
        RebuildPreview();
    }

    /// <summary>The adjusted luminance grid: contrast around mid-grey, brightness shift, invert.</summary>
    public double[] AdjustedLuminance()
    {
        if (_luminance is null) return Array.Empty<double>();
        double contrast = SldContrast?.Value ?? 1.0;
        double brightness = SldBrightness?.Value ?? 0.0;
        bool invert = ChkInvert?.IsChecked == true;

        var result = new double[_luminance.Length];
        for (int i = 0; i < _luminance.Length; i++)
        {
            double v = (_luminance[i] - 0.5) * contrast + 0.5 + brightness;
            v = Math.Clamp(v, 0.0, 1.0);
            result[i] = invert ? 1.0 - v : v;
        }
        return result;
    }

    // ---- live preview ----

    private void RebuildPreview()
    {
        if (_luminance is null || PreviewImage is null) return;
        var lum = AdjustedLuminance();

        int stride = (_width + 3) & ~3;
        var pixels = new byte[stride * _height];
        for (int y = 0; y < _height; y++)
            for (int x = 0; x < _width; x++)
                pixels[y * stride + x] = (byte)(lum[y * _width + x] * 255);

        var source = BitmapSource.Create(_width, _height, 96, 96, PixelFormats.Gray8, null,
            pixels, stride);
        PreviewImage.Source = source;

        double lo = lum.Min(), hi = lum.Max();
        PreviewNote.Text = $"{_width}×{_height} cells · range {lo:0.00}–{hi:0.00}" +
                           (hi - lo < 0.05 ? " · nearly uniform — check contrast" : "");
    }

    // ---- the three actions ----

    /// <summary>
    /// Shared path: build the heightfield for the chosen operation, stash it where the
    /// strategy layer reads it, create the toolpath, land on Toolpaths.
    /// </summary>
    public Toolpath? BuildToolpath(string strategyKey, HeightfieldData heightfield, string name)
    {
        var registry = new StrategyRegistry();
        var entry = registry.Find(strategyKey);
        if (entry is null) return null;

        var job = AppState.CurrentJob;
        var tp = new Toolpath { Name = name, StrategyKey = strategyKey };
        tp.ParamsJson = entry.DefaultsJson;

        var result = entry.Compute(System.Array.Empty<VectorShape>(), heightfield, tp.ParamsJson);
        if (!string.IsNullOrEmpty(result.Error))
            return null;   // honest refusal surfaced by the caller
        tp.GCode.AddRange(result.Gcode);
        tp.IsDirty = false;
        tp.EstimatedTimeSeconds = result.EstimatedTimeSeconds;

        AppState.Toolpaths.Toolpaths.Add(tp);
        CutsChanged?.Invoke();   // H-211: the Cuts list is a snapshot — tell it to rebuild
        return tp;
    }

    // ---- test seams (no production behaviour) ----

    public int WidthCells => _width;
    public int HeightCells => _height;
    public BitmapSource? PreviewSource => PreviewImage?.Source as BitmapSource;
    public string LastPhotoStatus() => PhotoStatus.Text;

    private HeightfieldData? HeightfieldForActions(double minMm, double maxMm)
    {
        var lum = AdjustedLuminance();
        if (_luminance is null || lum.Length == 0) return null;

        // Physical size: 0.2mm cells, capped to a sensible plaque width (~100mm).
        const double cell = 0.2;
        return new HeightfieldData(_width, _height, cell, 0, 0, lum.Select(v => minMm + v * (maxMm - minMm)).ToArray());
    }

    public void PhotoVCarve_Click(object sender, RoutedEventArgs e) => RunPhotoAction("photo-vcarve", "Photo V-carve");

    public void Lithophane_Click(object sender, RoutedEventArgs e) => RunLithophane();

    /// <summary>V-carve / relief consume the LUMINANCE as a 0..1 surface directly.</summary>
    private void RunPhotoAction(string strategyKey, string label)
    {
        var lum = AdjustedLuminance();
        if (lum.Length == 0)
        {
            PhotoStatus.Text = "import a photo first";
            return;
        }

        const double cell = 0.25;
        // Depth range for engraving-style ops: surface 0..3mm above zero.
        var hf = new HeightfieldData(_width, _height, cell, 0, 0,
            lum.Select(v => v * 3.0).ToArray());

        var tp = BuildToolpath(strategyKey, hf, $"{label} · {_fileName}");
        PhotoStatus.Text = tp is null
            ? $"{strategyKey} unavailable"
            : $"{label} added to Cuts ({tp.GCode.Count} lines)";
    }

    /// <summary>
    /// H-211: the grayscale relief is a MODEL action, not only a cut. The luminance
    /// becomes a ReliefComponent on the shared component stack (the same stack the
    /// Model stage's tree shows), then the same field goes through sketch-carve so
    /// the Cuts list gets a real mill program with G1 moves.
    /// </summary>
    public void Relief3D_Click(object sender, RoutedEventArgs e)
    {
        var lum = AdjustedLuminance();
        if (lum.Length == 0)
        {
            PhotoStatus.Text = "import a photo first";
            return;
        }

        const double cell = 0.25;
        var hf = new HeightfieldData(_width, _height, cell, 0, 0,
            lum.Select(v => v * 3.0).ToArray());

        // Land it on the component stack (shared with ModelPanel) and keep the job's
        // model heightfield in sync so downstream 3D strategies see it too.
        AppState.Components.Add(hf, $"Grayscale relief · {_fileName}");
        AppState.ModelHeightfield = AppState.Components.Composite;

        var tp = BuildToolpath("sketch-carve", hf, $"Photo relief · {_fileName}");
        PhotoStatus.Text = tp is null
            ? "sketch-carve unavailable"
            : $"Photo relief added to Cuts ({tp.GCode.Count} lines) + component stack";
    }

    /// <summary>Lithophane consumes luminance through the dedicated engine (dark → thick).</summary>
    private void RunLithophane()
    {
        var lum = AdjustedLuminance();
        if (lum.Length == 0)
        {
            PhotoStatus.Text = "import a photo first";
            return;
        }

        var p = new LithophaneParams();   // defaults: 0.8–3.5mm, dark→thicker
        var hf = LithophaneEngine.Compute(lum, _width, _height, p);
        if (hf is null)
        {
            PhotoStatus.Text = "could not build the lithophane plate from this image";
            return;
        }

        // H-211: the lithophane is a MILLED plate, not a laser job. The thickness field
        // goes through the finish3d registry entry (HeightfieldFinishEngine), which
        // raster-rows the surface and emits real G1 cutting moves. The old laser-picture
        // route emitted dot-burning G0/M3 lines — no G1, no mill program.
        var tp = BuildToolpath("finish3d", hf, $"Lithophane · {_fileName}");
        PhotoStatus.Text = tp is null
            ? "lithophane strategy unavailable"
            : $"Lithophane added to Cuts ({tp.GCode.Count} lines)";
    }
}
