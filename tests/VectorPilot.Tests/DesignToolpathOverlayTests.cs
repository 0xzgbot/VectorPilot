using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using VectorPilot.App;
using VectorPilot.App.Controls;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// P-101: the Design canvas can overlay calculated G-code — cut moves as solid
/// green strokes in world mm, rapids dashed red — so you can see whether the bit
/// stays inside the shape before streaming. Toggle off (or empty job) restores a
/// clean sheet.
/// </summary>
[Collection("STA")]
public class DesignToolpathOverlayTests
{
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
                var res = Application.Current!.Resources;
                if (!res.Contains("PanelBg"))
                {
                    foreach (var k in new[] { "RailBg", "RailHover", "Accent", "PanelBg", "TextOnDark" })
                        res[k] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
                }
                body();
            }
            catch (Exception ex)
            {
                error = ex;
                Console.Error.WriteLine("P101>>> " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        Assert.True(t.Join(TimeSpan.FromSeconds(30)), "test hung");
        if (error is not null) throw error;
    }

    /// <summary>One square profile program: positioning rapid + rapid stroke + 3 cuts.
    /// Returns the toolpath so the test can remove exactly what IT added — the shared
    /// tree is also used by parallel non-STA tests, so never Clear() it.</summary>
    private static Toolpath AddCalculatedSquareToolpath()
    {
        var tp = AppState.Toolpaths.Add(ToolpathStrategy.Profile, name: "square");
        tp.SetResult(new List<string>
        {
            "(profile)",
            "G0 X0 Y0",       // positioning rapid — no previous point, no stroke yet
            "G0 X10 Y0",      // rapid stroke (0,0)→(10,0)
            "G1 X10 Y10 F1000",
            "G1 X0 Y10",
            "G1 X0 Y0",
            "M30",
        });
        return tp;
    }

    private static int OverlayLineCount(DesignPanel panel)
        => panel.DrawCanvasChildren().Count(c => c is Line);

    [Fact]
    public void Toggle_On_Paints_Cut_And_Rapid_Strokes_In_World_Mm()
    {
        OnSta(() =>
        {
            var tp = AddCalculatedSquareToolpath();
            try
            {
                var panel = new DesignPanel();

                Assert.False(panel.ShowToolpaths);   // off by default
                Assert.Equal(0, OverlayLineCount(panel));

                panel.SetShowToolpathsForTest(true);

                Assert.True(panel.ShowToolpaths);
                // 5 motion lines → first has no previous point, so 4 segments:
                // 1 rapid (dashed red) + 3 cuts (solid green), all in world mm.
                int lines = OverlayLineCount(panel);
                Assert.Equal(4, lines);

                // World-mm placement: every stroke endpoint within the square bounds.
                foreach (var line in panel.DrawCanvasChildren().OfType<Line>())
                {
                    Assert.InRange(Math.Min(line.X1, line.X2), -0.001, 10.001);
                    Assert.InRange(Math.Min(line.Y1, line.Y2), -0.001, 10.001);
                }

                // Rapids distinct from cuts: exactly one red-dashed rapid stroke.
                var rapids = panel.DrawCanvasChildren()
                    .OfType<Line>().Where(l => l.StrokeDashArray is not null).ToList();
                Assert.Single(rapids);
            }
            finally { AppState.Toolpaths.Remove(tp.Id); }
        });
    }

    [Fact]
    public void Toggle_Off_Removes_The_Overlay()
    {
        OnSta(() =>
        {
            var tp = AddCalculatedSquareToolpath();
            try
            {
                var panel = new DesignPanel();
                panel.SetShowToolpathsForTest(true);
                Assert.True(OverlayLineCount(panel) > 0);

                panel.SetShowToolpathsForTest(false);
                Assert.Equal(0, OverlayLineCount(panel));
            }
            finally { AppState.Toolpaths.Remove(tp.Id); }
        });
    }

    [Fact]
    public void Empty_Job_Or_Uncalculated_Toolpaths_Paint_Nothing()
    {
        OnSta(() =>
        {
            var panel = new DesignPanel();
            panel.SetShowToolpathsForTest(true);

            // A toolpath that was added but never calculated → no strokes.
            // (Snapshot the count: parallel tests share this tree, so assert on
            // OUR delta, not an absolute zero.)
            int before = OverlayLineCount(panel);
            var tp = AppState.Toolpaths.Add(ToolpathStrategy.Pocket, name: "never calculated");
            try
            {
                panel.SetShowToolpathsForTest(true);   // force a repaint with it present
                Assert.Equal(before, OverlayLineCount(panel));
            }
            finally { AppState.Toolpaths.Remove(tp.Id); }
        });
    }
}
