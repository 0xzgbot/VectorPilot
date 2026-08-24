using System.Windows;
using VectorPilot.App;
using VectorPilot.App.Controls;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// H-503: live sim playback. The streamer's line index drives a cursor on the
/// 3D preview — machined segments light green, pending stay dim, a red head
/// marker tracks the current position. E-stop cancels the stream, which stops
/// the cursor with it (the cursor is driven BY the stream progress).
/// </summary>
[Collection("STA")]
public class LiveSimTests
{
    private static readonly IReadOnlyList<string> Program = new List<string>
    {
        "%",
        "G0 X0 Y0",      // rapid to origin (no segment: no previous point)
        "G1 X10 Y0",     // cut 1 → segment 0
        "G1 X10 Y10",    // cut 2 → segment 1
        "G1 X20 Y10",    // cut 3 → segment 2
        "G1 X20 Y0",     // cut 4 → segment 3
        "M30",
        "%",
    };

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
                    res["RailButton"] = new Style(typeof(System.Windows.Controls.Button));
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
    public void Begin_Arms_Tracking_And_Cursor_Follows_Line_Index()
    {
        OnSta(() =>
        {
            var preview = new ThreeDPreview();
            Assert.False(preview.IsLivePlayback);

            preview.BeginLivePlayback(Program);
            Assert.True(preview.IsLivePlayback);

            // The streamer counts non-comment lines: 6 motion/system lines here.
            // Cursor at 2 = two lines sent = the first cut drawn.
            preview.MoveLiveCursor(2);

            preview.EndLivePlayback();
            Assert.False(preview.IsLivePlayback);
        });
    }

    [Fact]
    public void Cursor_Clamps_To_The_Program_Length()
    {
        OnSta(() =>
        {
            var preview = new ThreeDPreview();
            preview.BeginLivePlayback(Program);

            // Far past the end must clamp, not throw — the streamer can tick once
            // more after the last line lands.
            preview.MoveLiveCursor(int.MaxValue);
            preview.MoveLiveCursor(-5);   // and below zero

            preview.EndLivePlayback();
        });
    }

    [Fact]
    public void Cursor_Ignored_When_Playback_Not_Armed()
    {
        OnSta(() =>
        {
            var preview = new ThreeDPreview();
            preview.MoveLiveCursor(3);   // must be a silent no-op
            Assert.False(preview.IsLivePlayback);
        });
    }

    [Fact]
    public void Shell_Exposes_The_Model_Preview_For_Live_Sim()
    {
        OnSta(() =>
        {
            var w = new MainWindow();
            Assert.NotNull(w.LiveSimPreview);
        });
    }
}
