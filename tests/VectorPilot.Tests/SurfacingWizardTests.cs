using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using VectorPilot.App;
using VectorPilot.App.Controls;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// H-402: the wasteboard surfacing wizard generates a serpentine raster facing
/// program covering the sheet XY and lands it in the Cuts list as a real Toolpath.
/// Nothing streams anywhere — Start stays with the user.
/// </summary>
[Collection("STA")]
public class SurfacingWizardTests
{
    private static WasteboardSurfacing.Params Params(double w = 400, double h = 300)
        => new() { SheetWidthMm = w, SheetHeightMm = h };

    [Fact]
    public void Raster_Covers_The_Sheet_With_Serpentine_Rows()
    {
        var r = WasteboardSurfacing.Generate(Params());

        Assert.True(r.RowCount >= 2, "a 400×300 sheet needs several raster rows");

        // Serpentine: rows alternate direction (X targets flip left/right).
        var rowTargets = r.GcodeLines
            .Where(l => Regex.IsMatch(l, @"^G1 X"))
            .Select(l => double.Parse(l.Split('X')[1].Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture))
            .ToList();
        Assert.True(rowTargets.Count >= 2);
        bool anyLeft = rowTargets.Any(x => x < 200), anyRight = rowTargets.Any(x => x > 200);
        Assert.True(anyLeft && anyRight, "serpentine must cut in BOTH directions");

        // Coverage: extreme X reaches both edges of the sheet.
        double minX = rowTargets.Min(), maxX = rowTargets.Max();
        Assert.True(minX <= 30 && maxX >= 370, $"rows span {minX}..{maxX} — not covering 400mm");
    }

    [Fact]
    public void Program_Is_A_Complete_Standalone_Facing_Program()
    {
        var r = WasteboardSurfacing.Generate(Params());

        Assert.Equal("%", r.GcodeLines[0]);
        Assert.Equal("%", r.GcodeLines[^1]);
        Assert.Contains(r.GcodeLines, l => l.StartsWith("O=WASTEBOARD_SURFACING"));
        Assert.Contains(r.GcodeLines, l => l.StartsWith("M3"));   // spindle on
        Assert.Contains(r.GcodeLines, l => l == "M5");            // spindle off
        Assert.Contains(r.GcodeLines, l => l == "M30");           // program end

        // Depth is negative (cutting INTO the board) and every plunge is guarded by safe-Z rapids.
        Assert.Contains(r.GcodeLines, l => Regex.IsMatch(l, @"^G1 Z-\d"));
        int plunges = r.GcodeLines.Count(l => l.StartsWith("G1 Z-"));
        int lifts = r.GcodeLines.Count(l => l.StartsWith("G0 Z5"));
        Assert.Equal(plunges + 1, lifts);   // +1: the program-header retract to safe Z
    }

    [Fact]
    public void Stepover_Drives_Row_Count()
    {
        // 300mm tall sheet: 40% of a 20mm cutter = 8mm stepover → ~37 rows;
        // 90% = 18mm → ~16 rows. Fewer, wider steps must mean fewer rows.
        var fine = WasteboardSurfacing.Generate(new WasteboardSurfacing.Params
        {
            SheetWidthMm = 400, SheetHeightMm = 300,
            CutterDiameterMm = 20, StepoverPercent = 40
        });
        var wide = WasteboardSurfacing.Generate(new WasteboardSurfacing.Params
        {
            SheetWidthMm = 400, SheetHeightMm = 300,
            CutterDiameterMm = 20, StepoverPercent = 90
        });

        Assert.True(fine.RowCount > wide.RowCount,
            $"{fine.RowCount} rows at 40% must exceed {wide.RowCount} at 90%");
    }

    [Fact]
    public void Wizard_Generates_And_Lands_A_Real_Cuts_Row()
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
                    // Seed what the shell XAML needs — whichever test constructs the
                    // Application first must leave it usable for everyone (the
                    // DpiScaling/JobStarter ordering lesson).
                    foreach (var k in new[] { "RailBg", "RailHover", "Accent", "PanelBg", "TextOnDark" })
                        res[k] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
                    res["RailButton"] = new Style(typeof(Button));
                }

                int before = AppState.Toolpaths.Toolpaths.Count;
                var dlg = new MachineDock().OpenSurfacingWizard();

                var tp = dlg.GenerateAndLand();

                Assert.NotNull(tp);
                Assert.Same(tp, dlg.Created);
                Assert.Equal(before + 1, AppState.Toolpaths.Toolpaths.Count);
                Assert.Contains("Wasteboard surfacing", tp!.Name);
                Assert.True(tp.GCode.Count > 10, "landed toolpath carries no program");
                Assert.Contains("O=WASTEBOARD_SURFACING", tp.GCode[1]);

                // cleanup — the shared tree outlives this test
                AppState.Toolpaths.Remove(tp.Id);
            }
            catch (Exception ex)
            {
                error = ex;
                Console.Error.WriteLine("INNER>>> " + ex + Environment.NewLine + ex.StackTrace);
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        Assert.True(t.Join(TimeSpan.FromSeconds(30)), "test hung");
        if (error is not null) throw error;
    }
}
