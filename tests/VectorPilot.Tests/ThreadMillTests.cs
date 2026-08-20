using System.Globalization;
using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Thread milling as a real strategy.
///
/// Aspire ships Thread Mill as BOTH a tool type and a toolpath. VectorPilot had
/// ToolType.ThreadMill in the tool database — and so does the Mac — but no thread
/// toolpath anywhere, so selecting a thread mill cut whatever other strategy happened
/// to be picked. These tests pin the helical geometry that makes it a thread.
/// </summary>
public class ThreadMillTests
{
    private static readonly StrategyRegistry Reg = new();

    private static List<VectorShape> Hole(double cx = 50, double cy = 50, double r = 6)
        => new() { VectorShape.Circle(new VectorPoint(cx, cy), r) };

    private static ThreadMillParams P() => new()
    {
        NominalDiameterMm = 12,
        PitchMm = 1.75,
        ThreadDepthMm = 10,
        ToolDiameterMm = 6,
        RadialPasses = 1,
        SegmentsPerRevolution = 24,
        FeedRateMmPerMin = 400,
        SafeZHeightMm = 5
    };

    private static List<(double X, double Y, double Z)> Moves(IEnumerable<string> gcode)
    {
        var pts = new List<(double, double, double)>();
        foreach (var l in gcode.Where(l => l.StartsWith("G1 X") && l.Contains('Z')))
        {
            var toks = l.Split(' ');
            double G(char c) => double.Parse(
                toks.First(t => t[0] == c)[1..], CultureInfo.InvariantCulture);
            pts.Add((G('X'), G('Y'), G('Z')));
        }
        return pts;
    }

    // ---- it is registered and reachable ----

    [Fact]
    public void Thread_Mill_Is_A_Registered_Strategy()
    {
        var entry = Reg.Find("threadmill");
        Assert.NotNull(entry);
        Assert.Equal("Thread Mill", entry!.DisplayName);
    }

    [Fact]
    public void The_Key_Maps_To_An_Enum_Case()
    {
        Assert.Equal(ToolpathStrategy.ThreadMill, StrategyKeyMap.ToStrategy("threadmill"));
        Assert.Equal("threadmill", StrategyKeyMap.ToKey(ToolpathStrategy.ThreadMill));
    }

    [Fact]
    public void It_Emits_Gcode_Through_The_Registry()
    {
        var entry = Reg.Find("threadmill")!;
        var result = entry.Compute(Hole(), null, entry.DefaultsJson);

        Assert.NotEmpty(result.Gcode);
        Assert.Null(result.Error);
    }

    // ---- the geometry is actually a thread ----

    [Fact]
    public void The_Path_Is_Helical_Not_Flat()
    {
        var moves = Moves(ThreadMillEngine.Compute(Hole(), P()).GcodeLines);

        Assert.NotEmpty(moves);
        Assert.True(moves.Select(m => m.Z).Distinct().Count() > 5,
            "Z never varies — that is a circular pocket, not a thread");
    }

    [Fact]
    public void Axial_Rise_Per_Revolution_Equals_The_Pitch()
    {
        var p = P();
        p.SegmentsPerRevolution = 36;
        var moves = Moves(ThreadMillEngine.Compute(Hole(), p).GcodeLines);

        // One full revolution is SegmentsPerRevolution points apart.
        double z0 = moves[0].Z;
        double z1 = moves[p.SegmentsPerRevolution].Z;

        Assert.Equal(p.PitchMm, z1 - z0, 2);
    }

    [Fact]
    public void The_Cut_Orbits_At_A_Constant_Radius()
    {
        var moves = Moves(ThreadMillEngine.Compute(Hole(), P()).GcodeLines);

        var radii = moves.Select(m => Math.Sqrt((m.X - 50) * (m.X - 50) + (m.Y - 50) * (m.Y - 50)))
                         .Where(r => r > 0.01)
                         .ToList();

        Assert.NotEmpty(radii);
        Assert.Equal(radii.Min(), radii.Max(), 2);   // a helix, not a spiral
    }

    [Fact]
    public void The_Orbit_Radius_Fits_Inside_The_Nominal_Thread()
    {
        var p = P();
        var moves = Moves(ThreadMillEngine.Compute(Hole(), p).GcodeLines);

        double maxR = moves.Max(m => Math.Sqrt((m.X - 50) * (m.X - 50) + (m.Y - 50) * (m.Y - 50)));
        // Tool centre + tool radius reaches the nominal radius exactly on the final
        // pass — that is the finished crest. It must not go BEYOND it.
        Assert.True(maxR + p.ToolDiameterMm / 2 <= p.NominalDiameterMm / 2 + 1e-3,
            $"cutter reaches {maxR + p.ToolDiameterMm / 2:F4}mm, past the {p.NominalDiameterMm / 2:F4}mm thread radius");
        Assert.True(maxR > 0.01, "the cutter never left the hole centre");
    }

    [Fact]
    public void It_Climbs_From_The_Bottom_Up()
    {
        // Finishing upward means the tool never drags back through a finished crest.
        var moves = Moves(ThreadMillEngine.Compute(Hole(), P()).GcodeLines);

        Assert.True(moves[^1].Z > moves[0].Z,
            "the cut ends deeper than it started — it is descending through finished thread");
    }

    [Fact]
    public void A_Left_Hand_Thread_Winds_The_Other_Way()
    {
        var right = P(); right.Hand = ThreadHand.RightHand;
        var left = P(); left.Hand = ThreadHand.LeftHand;

        var a = Moves(ThreadMillEngine.Compute(Hole(), right).GcodeLines);
        var b = Moves(ThreadMillEngine.Compute(Hole(), left).GcodeLines);

        // Second point differs in Y sign relative to centre.
        Assert.NotEqual(Math.Sign(a[0].Y - 50), Math.Sign(b[0].Y - 50));
    }

    [Fact]
    public void A_Finer_Pitch_Cuts_More_Revolutions()
    {
        var coarse = P(); coarse.PitchMm = 2.5;
        var fine = P(); fine.PitchMm = 1.0;

        int c = ThreadMillEngine.Compute(Hole(), coarse).RevolutionCount;
        int f = ThreadMillEngine.Compute(Hole(), fine).RevolutionCount;

        Assert.True(f > c, $"fine pitch={f} revs should exceed coarse={c}");
    }

    [Fact]
    public void More_Radial_Passes_Emit_More_Moves()
    {
        var one = P(); one.RadialPasses = 1;
        var three = P(); three.RadialPasses = 3;

        Assert.True(ThreadMillEngine.Compute(Hole(), three).GcodeLines.Count >
                    ThreadMillEngine.Compute(Hole(), one).GcodeLines.Count);
    }

    [Fact]
    public void Multiple_Holes_Are_All_Threaded()
    {
        var holes = new List<VectorShape>
        {
            VectorShape.Circle(new VectorPoint(20, 20), 6),
            VectorShape.Circle(new VectorPoint(80, 60), 6)
        };

        var moves = Moves(ThreadMillEngine.Compute(holes, P()).GcodeLines);

        Assert.Contains(moves, m => m.X < 50);
        Assert.Contains(moves, m => m.X > 50);
    }

    // ---- refusals, not bad cuts ----

    [Fact]
    public void A_Tool_Too_Wide_For_The_Hole_Is_Refused()
    {
        var p = P();
        p.ToolDiameterMm = 14;      // wider than the 12mm thread
        var r = ThreadMillEngine.Compute(Hole(), p);

        Assert.Empty(r.GcodeLines);
        Assert.False(string.IsNullOrWhiteSpace(r.Error));
        Assert.Contains("does not fit", r.Error!);
    }

    [Fact]
    public void No_Holes_Is_Refused_With_A_Reason()
    {
        var r = ThreadMillEngine.Compute(new List<VectorShape>(), P());

        Assert.Empty(r.GcodeLines);
        Assert.Contains("needs at least one hole", r.Error!);
    }

    [Fact]
    public void A_Zero_Pitch_Is_Refused()
    {
        var p = P(); p.PitchMm = 0;
        var r = ThreadMillEngine.Compute(Hole(), p);

        Assert.Empty(r.GcodeLines);
        Assert.Contains("pitch", r.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_Program_Starts_And_Stops_The_Spindle()
    {
        var g = ThreadMillEngine.Compute(Hole(), P()).GcodeLines;

        Assert.Contains(g, l => l.StartsWith("M3"));
        Assert.Contains("M5", g);
    }
}
