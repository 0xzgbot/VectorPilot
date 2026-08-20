using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Serial;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Units: the selected machine profile must decide the posted modal, and the posted modal
/// must match what the DRO and the operator are reading.
///
/// The README claimed "the serial layer streams in inches (G20); the engine layer works in mm".
/// That is false — and it is the most dangerous kind of false, because a 25.4x scale error
/// destroys stock and tooling. The engine works in mm, PostSelector maps
/// MachineProfile.Units to GCodeUnits, and the post emits the matching modal. An mm profile
/// streams G21, not G20. These tests pin that mapping so the doc cannot drift back.
/// </summary>
public class MachineUnitsMappingTests
{
    private static MachineProfile Profile(MachineUnits units, MachineType type = MachineType.Grbl)
        => new() { Name = "Test", Units = units, MachineType = type };

    private static List<string> Post(GCodeUnits units)
        => PostTemplateEngine.Emit(new[] { "G1 X10 Y10 F600" }, PostTemplate.Grbl(units)).Lines;

    private static bool Has(IEnumerable<string> lines, string code)
        => lines.Any(l => l.Contains(code, StringComparison.Ordinal));

    // ---- profile units decide the posted modal ----

    [Fact]
    public void An_Mm_Profile_Selects_Millimetre_Output()
    {
        var (_, units, _) = PostSelector.ForProfile(Profile(MachineUnits.Millimeter));
        Assert.Equal(GCodeUnits.Millimeter, units);
    }

    [Fact]
    public void An_Inch_Profile_Selects_Inch_Output()
    {
        var (_, units, _) = PostSelector.ForProfile(Profile(MachineUnits.Inch));
        Assert.Equal(GCodeUnits.Inch, units);
    }

    [Fact]
    public void An_Mm_Profile_Posts_G21_And_Never_G20()
    {
        var (_, units, _) = PostSelector.ForProfile(Profile(MachineUnits.Millimeter));
        var posted = Post(units);

        Assert.True(Has(posted, "G21"), "an mm machine did not get G21");
        Assert.False(Has(posted, "G20"), "an mm machine was sent G20 — a 25.4x scale error");
    }

    [Fact]
    public void An_Inch_Profile_Posts_G20_And_Never_G21()
    {
        var (_, units, _) = PostSelector.ForProfile(Profile(MachineUnits.Inch));
        var posted = Post(units);

        Assert.True(Has(posted, "G20"), "an inch machine did not get G20");
        Assert.False(Has(posted, "G21"), "an inch machine was sent G21 — a 25.4x scale error");
    }

    [Fact]
    public void The_Two_Unit_Modes_Produce_Different_Programs()
    {
        Assert.NotEqual(
            string.Join("\n", Post(GCodeUnits.Millimeter)),
            string.Join("\n", Post(GCodeUnits.Inch)));
    }

    [Fact]
    public void The_Units_Modal_Comes_Before_Any_Motion()
    {
        // A controller that sees G1 before G21 interprets that move in whatever mode it was
        // left in — which is exactly how a job destroys stock on the first cut.
        var posted = Post(GCodeUnits.Millimeter);

        static string Strip(string l)
        {
            var s = l.TrimStart();
            if (s.Length > 0 && (s[0] == 'N' || s[0] == 'n'))
            {
                int sp = s.IndexOf(' ');
                if (sp > 0) s = s[(sp + 1)..].TrimStart();
            }
            return s;
        }

        int modal = posted.FindIndex(l => Strip(l).StartsWith("G21", StringComparison.Ordinal));

        // "G21" also matches inside a header COMMENT, and a G1 move can sit on the same
        // numbered line as other words — so compare on the stripped code, and treat a
        // cutting move as one whose stripped text STARTS with G1/G2/G3 (not G21).
        int firstMove = posted.FindIndex(l =>
        {
            var s = Strip(l);
            if (s.StartsWith("G21", StringComparison.Ordinal)) return false;
            return s.StartsWith("G1", StringComparison.Ordinal)
                || s.StartsWith("G2", StringComparison.Ordinal)
                || s.StartsWith("G3", StringComparison.Ordinal);
        });

        Assert.True(modal >= 0, "no units modal was emitted at all");
        Assert.True(firstMove < 0 || modal < firstMove,
            $"units modal at line {modal} came after the first motion at {firstMove}");
    }

    [Fact]
    public void The_Post_Ids_Distinguish_The_Two()
    {
        Assert.Equal("grbl-mm", PostTemplate.Grbl(GCodeUnits.Millimeter).Id);
        Assert.Equal("grbl-in", PostTemplate.Grbl(GCodeUnits.Inch).Id);
    }

    // ---- the extension and post type follow the profile too ----

    [Fact]
    public void A_Grbl_Profile_Gets_The_Grbl_Post()
    {
        var (post, _, ext) = PostSelector.ForProfile(Profile(MachineUnits.Millimeter, MachineType.Grbl));

        Assert.Equal(PostProcessorType.Grbl, post);
        Assert.Equal("gcode", ext);
    }

    [Fact]
    public void A_Universal_Profile_Gets_The_Universal_Post()
    {
        var (post, _, ext) = PostSelector.ForProfile(Profile(MachineUnits.Millimeter, MachineType.Universal));

        Assert.Equal(PostProcessorType.Universal, post);
        Assert.Equal("nc", ext);
    }

    [Fact]
    public void Units_Are_Independent_Of_Machine_Type()
    {
        // An inch Universal machine must still get G20.
        var (_, units, _) = PostSelector.ForProfile(Profile(MachineUnits.Inch, MachineType.Universal));
        Assert.Equal(GCodeUnits.Inch, units);
    }

    // ---- a real posted job carries the modal the profile asked for ----

    [Fact]
    public void A_Calculated_Job_Posts_In_The_Profiles_Units()
    {
        var reg = new StrategyRegistry();
        var entry = reg.Find("profile")!;
        var gcode = entry.Compute(
            new[] { VectorPilot.Geometry.VectorShape.Rectangle(0, 0, 100, 60) },
            null, entry.DefaultsJson).Gcode;

        foreach (var (machine, expect, forbid) in new[]
        {
            (MachineUnits.Millimeter, "G21", "G20"),
            (MachineUnits.Inch, "G20", "G21")
        })
        {
            var (_, units, _) = PostSelector.ForProfile(Profile(machine));
            var posted = PostTemplateEngine.Emit(gcode, PostTemplate.Grbl(units)).Lines;

            Assert.True(Has(posted, expect), $"{machine} job missing {expect}");
            Assert.False(Has(posted, forbid), $"{machine} job contained {forbid}");
        }
    }
}
