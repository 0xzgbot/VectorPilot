using VectorPilot.App;
using VectorPilot.App.Controls;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// The preflight gate that Cut's Calculate and Machine's Start now SHARE.
///
/// They used to disagree: Cut checked whether an area strategy had a closed outline, while
/// MachinePanel.StreamStart_Click streamed AppState.LoadedGCode with no validation at all.
/// A comment-only program is the dangerous case — it looks runnable, the controller accepts
/// it, and the operator watches a job that never cuts.
/// </summary>
public class PreflightGateTests
{
    private static readonly StrategyRegistry Reg = new();

    private static VectorShape ClosedRect() => VectorShape.Rectangle(0, 0, 100, 60);

    private static VectorShape OpenPath()
        => VectorShape.Polyline(new List<VectorPoint> { new(0, 0), new(50, 0), new(50, 40) }, closed: false);

    // ---- open-only selection cannot produce pocket G-code ----

    [Fact]
    public void An_Open_Only_Selection_Cannot_Produce_Pocket_Gcode()
    {
        var why = JobGate.AreaStrategyBlocker("pocket", "Pocket", new[] { OpenPath() });

        Assert.NotNull(why);
        Assert.Contains("closed outline", why!);
    }

    [Fact]
    public void Profile_And_VCarve_Are_Blocked_Too()
    {
        Assert.NotNull(JobGate.AreaStrategyBlocker("profile", "Profile", new[] { OpenPath() }));
        Assert.NotNull(JobGate.AreaStrategyBlocker("vcarve", "V-Carve", new[] { OpenPath() }));
    }

    [Fact]
    public void A_Closed_Rectangle_Can_Produce_Pocket_Gcode()
    {
        Assert.Null(JobGate.AreaStrategyBlocker("pocket", "Pocket", new[] { ClosedRect() }));

        var entry = Reg.Find("pocket")!;
        var result = entry.Compute(new[] { ClosedRect() }, null, entry.DefaultsJson);

        Assert.NotEmpty(result.Gcode);
        Assert.Contains(result.Gcode, l => l.TrimStart().StartsWith("G1"));
    }

    [Fact]
    public void Cut_And_The_Gate_Give_The_Identical_Message()
    {
        // CutPanel delegates to JobGate, so the Machine stage cannot drift from it.
        var shapes = new[] { OpenPath() };

        Assert.Equal(
            JobGate.AreaStrategyBlocker("pocket", "Pocket", shapes),
            CutPanel.AreaStrategyBlocker("pocket", "Pocket", shapes));
    }

    [Fact]
    public void An_Engraving_Strategy_Is_Not_Blocked_On_An_Open_Path()
    {
        Assert.Null(JobGate.AreaStrategyBlocker("quick-engrave", "Quick Engrave", new[] { OpenPath() }));
        Assert.Null(JobGate.AreaStrategyBlocker("dragknife", "Drag Knife", new[] { OpenPath() }));
    }

    [Fact]
    public void A_Mixed_Selection_Is_Allowed()
    {
        Assert.Null(JobGate.AreaStrategyBlocker("pocket", "Pocket",
            new[] { OpenPath(), ClosedRect() }));
    }

    [Fact]
    public void Circles_And_Rectangles_Are_Implicitly_Closed()
    {
        Assert.False(JobGate.IsOpen(ClosedRect()));
        Assert.False(JobGate.IsOpen(VectorShape.Circle(new VectorPoint(0, 0), 20)));
        Assert.True(JobGate.IsOpen(OpenPath()));
    }

    // ---- Machine Start is blocked by the same gate ----

    [Fact]
    public void Start_Is_Blocked_For_An_Empty_Program()
    {
        var why = JobGate.StreamBlocker(Array.Empty<string>());

        Assert.NotNull(why);
        Assert.Contains("Nothing to stream", why!);
    }

    [Fact]
    public void Start_Is_Blocked_For_A_Comment_Only_Program()
    {
        // Exactly what a refused Calculate leaves behind: a valid-looking file with no cuts.
        var program = new List<string>
        {
            "%",
            "(Pocket: needs a closed outline — 1 selected shape(s) are open paths.)",
            "M5",
            "M30"
        };

        var why = JobGate.StreamBlocker(program);

        Assert.NotNull(why);
        Assert.Contains("no cutting moves", why!);
    }

    [Fact]
    public void Start_Is_Allowed_For_A_Real_Program()
    {
        var entry = Reg.Find("profile")!;
        var gcode = entry.Compute(new[] { ClosedRect() }, null, entry.DefaultsJson).Gcode;

        Assert.Null(JobGate.StreamBlocker(gcode));
    }

    [Fact]
    public void Arc_Only_Programs_Are_Allowed()
    {
        // G2/G3 are cutting moves too — a circle-only program must not be refused.
        var program = new List<string> { "G21", "G90", "G2 X10 Y10 I5 J0 F600", "M5" };

        Assert.Null(JobGate.StreamBlocker(program));
    }

    [Fact]
    public void The_Refused_Program_From_Calculate_Cannot_Be_Streamed()
    {
        // End to end: Cut refuses -> writes a comment -> Machine refuses the same job.
        var entry = Reg.Find("pocket")!;
        var shapes = new[] { OpenPath() };

        var blocker = JobGate.AreaStrategyBlocker(entry.Key, entry.DisplayName, shapes);
        Assert.NotNull(blocker);

        // This is the program CutPanel leaves in the toolpath when it refuses.
        var refused = new List<string> { $"({entry.DisplayName}: {blocker})" };

        Assert.NotNull(JobGate.StreamBlocker(refused));
    }

    // ---- the doctor reports geometry problems ----

    [Fact]
    public void The_Doctor_Flags_An_Open_Contour()
    {
        var issues = JobGate.Diagnose(new[] { OpenPath() });
        Assert.NotEmpty(issues);
    }

    [Fact]
    public void The_Doctor_Passes_A_Closed_Rectangle()
    {
        var issues = JobGate.Diagnose(new[] { ClosedRect() });
        Assert.Empty(issues);
    }

    [Fact]
    public void The_Doctor_Handles_An_Empty_Layer()
    {
        Assert.Empty(JobGate.Diagnose(Array.Empty<VectorShape>()));
    }
}
