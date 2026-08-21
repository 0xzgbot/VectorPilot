using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using VectorPilot.App;
using VectorPilot.App.Controls;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// H-211: every photo action must land a REAL mill toolpath in the Cuts list —
/// G1 moves through StrategyRegistry.Compute, a Toolpath object visible as a
/// Cuts ListView row after RefreshCutsList. The old lithophane route emitted
/// laser dot lines (G0 + M3) and no Cuts row ever appeared; that is the lie
/// this file kills.
/// </summary>
[Collection("STA")]
public class PhotoCncTests
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

    /// <summary>A real PNG: left half black, right half white — maximum depth variation.</summary>
    private static string WriteStepPng(string dir)
    {
        int w = 64, h = 48, stride = (w + 3) & ~3;
        var pixels = new byte[stride * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * stride + x] = x < w / 2 ? (byte)0 : (byte)255;

        var source = BitmapSource.Create(w, h, 96, 96, System.Windows.Media.PixelFormats.Gray8, null, pixels, stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        string path = Path.Combine(dir, $"step-{Guid.NewGuid():N}.png");
        using var fs = File.Create(path);
        encoder.Save(fs);
        return path;
    }

    /// <summary>Construct the REAL CutPanel and prove the new toolpath is a row in it.</summary>
    private static void AssertVisibleAsCutsRow(Toolpath tp)
    {
        var cutPanel = new CutPanel();
        cutPanel.RefreshCutsList();
        Assert.Contains(cutPanel.ToolpathListViewItems(), row => ReferenceEquals(row, tp));
    }

    /// <summary>
    /// Snapshot of toolpath ids, taken before an action. Other test collections share
    /// the static AppState list in parallel — identity, not index or count, is the only
    /// safe way to find OUR toolpath afterwards.
    /// </summary>
    private static HashSet<Guid> SnapshotIds() =>
        new(AppState.Toolpaths.Toolpaths.Select(t => t.Id));

    /// <summary>The newest toolpath added since the snapshot (ours, not a peer's).</summary>
    private static Toolpath? AddedSince(HashSet<Guid> before) => AppState.Toolpaths.Toolpaths
        .Where(t => !before.Contains(t.Id))
        .OrderByDescending(t => t.Id)
        .FirstOrDefault();

    [Fact]
    public void Photo_VCarve_Goes_Through_The_Registry_And_Lands_As_A_Cuts_Row()
    {
        OnSta(() =>
        {
            var panel = new PhotoPanel();
            panel.LoadImage(WriteStepPng(Path.GetTempPath()));
            var before = SnapshotIds();

            panel.PhotoVCarve_Click(null!, null!);

            var tp = AddedSince(before);
            Assert.NotNull(tp);
            // Registry-computed mill program: real cutting moves, never an empty % stub.
            Assert.True(tp!.GCode.Count > 2, "photo V-carve produced no program");
            Assert.Contains(tp.GCode, l => l.TrimStart().StartsWith("G1"));

            AssertVisibleAsCutsRow(tp);

            AppState.Toolpaths.Toolpaths.Remove(tp);
        });
    }

    [Fact]
    public void Lithophane_Is_A_Milled_Plate_Not_A_Laser_Job()
    {
        OnSta(() =>
        {
            var panel = new PhotoPanel();
            panel.LoadImage(WriteStepPng(Path.GetTempPath()));
            var before = SnapshotIds();

            panel.Lithophane_Click(null!, null!);

            var tp = AddedSince(before);
            Assert.NotNull(tp);
            // The lie this card kills was StrategyKey == "laser-picture" with G0+M3 dots.
            // finish3d emits a mill raster: O=FINISH_3D header plus G1 feed moves.
            Assert.Equal("finish3d", tp!.StrategyKey);
            Assert.Contains(tp.GCode, l => l.TrimStart().StartsWith("O=FINISH_3D"));
            // A milled plate cuts: G1 moves exist.
            Assert.Contains(tp.GCode, l => l.TrimStart().StartsWith("G1"));
            Assert.True(tp.GCode.Count > 10, $"lithophane program too thin to be a raster finish ({tp.GCode.Count} lines)");

            AssertVisibleAsCutsRow(tp);

            AppState.Toolpaths.Toolpaths.Remove(tp);
        });
    }

    [Fact]
    public void Grayscale_Relief_Adds_A_Component_And_A_Cuts_Row_With_Mill_Moves()
    {
        OnSta(() =>
        {
            var panel = new PhotoPanel();
            panel.LoadImage(WriteStepPng(Path.GetTempPath()));
            var before = SnapshotIds();
            int componentsBefore = AppState.Components.Components.Count;

            panel.Relief3D_Click(null!, null!);

            // The MODEL side: a grayscale component on the shared stack.
            Assert.True(AppState.Components.Components.Count == componentsBefore + 1,
                "relief did not land a component on the shared stack");

            // The CUT side: sketch-carve through Compute emits mill G1 into the Cuts list.
            var tp = AddedSince(before);
            Assert.NotNull(tp);
            Assert.Equal("sketch-carve", tp!.StrategyKey);
            Assert.Contains(tp.GCode, l => l.TrimStart().StartsWith("G1"));

            AssertVisibleAsCutsRow(tp);

            AppState.Toolpaths.Toolpaths.Remove(tp);
            AppState.Components.Components.RemoveAt(AppState.Components.Components.Count - 1);
        });
    }

    [Fact]
    public void All_Three_Photo_Strategies_Compute_Real_G1_Programs_In_The_Registry()
    {
        // Engine-level honesty check against the exact registry entries the buttons use:
        // no entry may answer with an empty "%" program when fed a real heightfield.
        var registry = new StrategyRegistry();
        int w = 32, h = 24;
        var hf = new HeightfieldData(w, h, 0.25, 0, 0,
            Enumerable.Range(0, w * h).Select(i => (i % w) / (double)w * 3.0).ToArray());

        foreach (var key in new[] { "photo-vcarve", "finish3d", "sketch-carve" })
        {
            var entry = registry.Find(key)!;
            var result = entry.Compute(Array.Empty<VectorShape>(), hf, entry.DefaultsJson);

            Assert.True(string.IsNullOrEmpty(result.Error), $"{key} refused: {result.Error}");
            Assert.True(result.Gcode.Count > 2, $"{key} produced no program");
            Assert.Contains(result.Gcode, l => l.TrimStart().StartsWith("G1"));
        }
    }

    [Fact]
    public void MainWindow_Subscribes_Photo_CutsChanged_To_The_Cuts_List()
    {
        OnSta(() =>
        {
            var w = new MainWindow();
            var photoField = typeof(MainWindow).GetField("_photo",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            var photo = (PhotoPanel)photoField!.GetValue(w)!;

            // Drive the event and watch the snapshot list rebuild: add a CLEAN probe
            // toolpath directly, raise CutsChanged, and the CutPanel owned by the
            // window must show it without any navigation.
            var probe = new Toolpath { Name = "H-211 probe", IsDirty = false };
            AppState.Toolpaths.Toolpaths.Add(probe);
            try
            {
                photo.RaiseCutsChangedForTest();

                var cutField = typeof(MainWindow).GetField("_cut",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
                var cut = (CutPanel)cutField.GetValue(w)!;
                Assert.Contains(cut.ToolpathListViewItems(), row => ReferenceEquals(row, probe));
            }
            finally
            {
                AppState.Toolpaths.Toolpaths.Remove(probe);
            }
        });
    }
}
