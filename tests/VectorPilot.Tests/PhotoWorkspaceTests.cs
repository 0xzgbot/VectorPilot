using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VectorPilot.App;
using VectorPilot.App.Controls;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// H-210: the photo workspace. Import a real image → adjust → SEE the heightfield → send it
/// to Photo V-carve / Lithophane / 3D relief, each landing as a Cuts-list toolpath with real
/// G-code. The empty-image path must be an honest refusal, never fake G-code.
/// </summary>
[Collection("STA")]
public class PhotoWorkspaceTests
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
                    foreach (var k in new[] { "RailBg", "RailHover", "Accent", "PanelBg", "TextOnDark" })
                        res[k] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
                    res["RailButton"] = new Style(typeof(Button));
                }
                body();
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw error;
    }

    /// <summary>A real PNG on disk: left half black, right half white — a step gradient with
    /// maximum depth variation, written through WPF's own encoder.</summary>
    private static string WriteStepPng(string dir)
    {
        int w = 64, h = 48, stride = (w + 3) & ~3;
        var pixels = new byte[stride * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * stride + x] = x < w / 2 ? (byte)0 : (byte)255;

        var source = BitmapSource.Create(w, h, 96, 96, PixelFormats.Gray8, null, pixels, stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        string path = Path.Combine(dir, "step-photo.png");
        using var fs = File.Create(path);
        encoder.Save(fs);
        return path;
    }

    [Fact]
    public void A_Real_Png_Loads_Into_A_Luminance_Grid_And_Preview()
    {
        OnSta(() =>
        {
            var panel = new PhotoPanel();
            panel.LoadImage(WriteStepPng(Path.GetTempPath()));

            Assert.Equal(64, panel.WidthCells);
            Assert.Equal(48, panel.HeightCells);
            Assert.NotEmpty(panel.AdjustedLuminance());

            // The preview is a real rendered bitmap, not an empty box.
            Assert.NotNull(panel.PreviewSource);
            Assert.Equal(64, panel.PreviewSource!.PixelWidth);

            // Buttons wake up only once an image exists.
            Assert.True(((Button)panel.FindName("BtnPhotoVCarve")!).IsEnabled);
            Assert.True(((Button)panel.FindName("BtnLithophane")!).IsEnabled);
            Assert.True(((Button)panel.FindName("BtnRelief3D")!).IsEnabled);
        });
    }

    [Fact]
    public void The_Preview_Shows_Depth_Variation_Not_A_Flat_Box()
    {
        // The vision AC, as pixels: a step photo must produce a preview whose dark and light
        // halves differ. A "gray rectangle" bug would make every pixel identical.
        OnSta(() =>
        {
            var panel = new PhotoPanel();
            panel.LoadImage(WriteStepPng(Path.GetTempPath()));

            var source = panel.PreviewSource!;
            int stride = (source.PixelWidth + 3) & ~3;
            var pixels = new byte[stride * source.PixelHeight];
            source.CopyPixels(pixels, stride, 0);

            double leftAvg = 0, rightAvg = 0;
            int half = source.PixelWidth / 2, rows = source.PixelHeight;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < half; x++) leftAvg += pixels[y * stride + x];
                for (int x = half; x < source.PixelWidth; x++) rightAvg += pixels[y * stride + x];
            }
            leftAvg /= half * rows;
            rightAvg /= (source.PixelWidth - half) * rows;

            Assert.True(Math.Abs(leftAvg - rightAvg) > 100,
                $"preview halves differ by only {Math.Abs(leftAvg - rightAvg):0.0} — flat preview");
        });
    }

    [Fact]
    public void Invert_Flips_The_Luminance_Grid()
    {
        OnSta(() =>
        {
            var panel = new PhotoPanel();
            panel.LoadImage(WriteStepPng(Path.GetTempPath()));
            var before = panel.AdjustedLuminance();

            ((System.Windows.Controls.Primitives.ToggleButton)panel.FindName("ChkInvert")!).IsChecked = true;
            var after = panel.AdjustedLuminance();

            Assert.Equal(before.Length, after.Length);
            for (int i = 0; i < before.Length; i++)
                Assert.Equal(1.0, before[i] + after[i], 3);
        });
    }

    [Fact]
    public void Photo_VCarve_Lands_A_Real_Toolpath_With_G1()
    {
        OnSta(() =>
        {
            var panel = new PhotoPanel();
            panel.LoadImage(WriteStepPng(Path.GetTempPath()));
            int before = AppState.Toolpaths.Toolpaths.Count;

            panel.PhotoVCarve_Click(null!, null!);

            Assert.Equal(before + 1, AppState.Toolpaths.Toolpaths.Count);
            var tp = AppState.Toolpaths.Toolpaths[^1];
            Assert.Equal("photo-vcarve", tp.StrategyKey);
            Assert.Contains(tp.GCode, l => l.TrimStart().StartsWith("G1"));

            AppState.Toolpaths.Toolpaths.Remove(tp);
        });
    }

    [Fact]
    public void Lithophane_Lands_A_Real_Toolpath_With_G1()
    {
        OnSta(() =>
        {
            var panel = new PhotoPanel();
            panel.LoadImage(WriteStepPng(Path.GetTempPath()));
            int before = AppState.Toolpaths.Toolpaths.Count;

            panel.Lithophane_Click(null!, null!);

            Assert.Equal(before + 1, AppState.Toolpaths.Toolpaths.Count);
            var tp = AppState.Toolpaths.Toolpaths[^1];
            // H-211: the lithophane is a MILLED plate — the thickness field runs through
            // finish3d (HeightfieldFinishEngine), which raster-rows the surface and emits
            // real G1 cutting moves. The old laser-picture route (G0 dots + M3 power
            // pulses, no G1, no Cuts row) is gone.
            Assert.Equal("finish3d", tp.StrategyKey);
            Assert.Contains(tp.GCode, l => l.TrimStart().StartsWith("G1"));

            AppState.Toolpaths.Toolpaths.Remove(tp);
        });
    }

    [Fact]
    public void Relief3D_Lands_A_Real_Toolpath_With_G1()
    {
        OnSta(() =>
        {
            var panel = new PhotoPanel();
            panel.LoadImage(WriteStepPng(Path.GetTempPath()));
            int before = AppState.Toolpaths.Toolpaths.Count;
            int componentsBefore = AppState.Components.Components.Count;

            panel.Relief3D_Click(null!, null!);

            Assert.Equal(before + 1, AppState.Toolpaths.Toolpaths.Count);
            var tp = AppState.Toolpaths.Toolpaths[^1];
            Assert.Equal("sketch-carve", tp.StrategyKey);
            Assert.Contains(tp.GCode, l => l.TrimStart().StartsWith("G1"));
            // H-211: the relief is also a MODEL action — a grayscale component lands
            // on the shared stack.
            Assert.Equal(componentsBefore + 1, AppState.Components.Components.Count);

            AppState.Toolpaths.Toolpaths.Remove(tp);
            AppState.Components.Components.RemoveAt(AppState.Components.Components.Count - 1);
        });
    }

    [Fact]
    public void Actions_Before_An_Image_Are_An_Honest_Refusal()
    {
        OnSta(() =>
        {
            var panel = new PhotoPanel();
            int before = AppState.Toolpaths.Toolpaths.Count;

            panel.PhotoVCarve_Click(null!, null!);

            Assert.Equal(before, AppState.Toolpaths.Toolpaths.Count);   // no fake toolpath
            Assert.Contains("import a photo", panel.LastPhotoStatus(), StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void The_Strategy_Layer_Refuses_Without_A_Heightfield()
    {
        // The registry's own honest Empty() path: photo-vcarve with a null heightfield must
        // return an Error, never a fake program.
        var registry = new StrategyRegistry();
        var entry = registry.Find("photo-vcarve")!;
        var result = entry.Compute(Array.Empty<VectorShape>(), null, entry.DefaultsJson);

        Assert.False(string.IsNullOrEmpty(result.Error));
        Assert.True(result.Gcode.Count == 0 || result.Error.Length > 0);
    }

    [Fact]
    public void The_Photo_Stage_Switches_From_The_Rail_Tag()
    {
        OnSta(() =>
        {
            var w = new MainWindow();

            // Drive the same dispatch the rail button uses, then check what the stage host
            // now shows — the visual tree needs a Show() we don't want in tests.
            typeof(MainWindow).GetMethod("Stage_ClickByTag",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(w, new object?[] { "photo" });

            var host = (ContentControl)w.FindName("StageHost")!;
            Assert.IsType<PhotoPanel>(host.Content);
        });
    }
}
