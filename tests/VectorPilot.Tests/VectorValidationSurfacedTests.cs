using VectorPilot.App;
using VectorPilot.App.Controls;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Vector validation surfaced in the UI.
///
/// VectorValidator existed with no VectorPilot.App call-site, so open vectors and
/// self-intersections reached Calculate silently and produced junk G-code. Design now has
/// a Validate button (DesignPanel.DoValidate) and Cut refuses area strategies on an
/// all-open selection with an explanation.
/// </summary>
public class VectorValidationSurfacedTests
{
    private static readonly StrategyRegistry Reg = new();

    private static VectorShape OpenPolyline()
        => VectorShape.Polyline(new List<VectorPoint>
        {
            new(0, 0), new(50, 0), new(50, 40)
        }, closed: false);

    private static VectorShape ClosedRect() => VectorShape.Rectangle(0, 0, 100, 60);

    private static VectorShape SelfIntersecting()
        // Bow-tie: edges cross in the middle.
        => VectorShape.Polyline(new List<VectorPoint>
        {
            new(0, 0), new(100, 100), new(100, 0), new(0, 100)
        }, closed: true);

    // ---- the validator flags what it should ----

    [Fact]
    public void An_Open_Polyline_Is_Flagged()
    {
        var issues = VectorValidator.Validate(new[] { OpenPolyline() });

        Assert.NotEmpty(issues);
        Assert.Contains(issues, i => i.Message.Contains("open", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_Closed_Rectangle_Is_Clean()
    {
        var issues = VectorValidator.Validate(new[] { ClosedRect() });

        Assert.Empty(issues);
    }

    [Fact]
    public void A_Self_Intersecting_Outline_Is_Detected()
    {
        Assert.True(VectorValidator.HasSelfIntersection(SelfIntersecting()),
            "a bow-tie outline was not reported as self-intersecting");
    }

    [Fact]
    public void A_Simple_Rectangle_Does_Not_Self_Intersect()
    {
        Assert.False(VectorValidator.HasSelfIntersection(ClosedRect()));
    }

    [Fact]
    public void Issues_Point_At_The_Offending_Shape()
    {
        // Shape 0 is clean, shape 1 is open: the issue must name index 1 so the UI can
        // select the right offender.
        var issues = VectorValidator.Validate(new[] { ClosedRect(), OpenPolyline() });

        Assert.NotEmpty(issues);
        Assert.Contains(issues, i => i.ShapeIndex == 1);
        Assert.DoesNotContain(issues, i => i.ShapeIndex == 0);
    }

    [Fact]
    public void A_Mixed_Layer_Reports_Only_The_Bad_Shapes()
    {
        var shapes = new[] { ClosedRect(), OpenPolyline(), ClosedRect(), OpenPolyline() };
        var flagged = VectorValidator.Validate(shapes).Select(i => i.ShapeIndex).Distinct().ToList();

        Assert.Equal(new[] { 1, 3 }, flagged.OrderBy(i => i).ToArray());
    }

    // ---- Calculate explains instead of emitting junk ----

    [Fact]
    public void Profile_Of_An_All_Open_Selection_Is_Blocked_With_A_Reason()
    {
        var why = CutPanel.AreaStrategyBlocker("profile", "Profile", new[] { OpenPolyline() });

        Assert.NotNull(why);
        Assert.Contains("closed outline", why!);
        Assert.Contains("Extend", why!);
    }

    [Fact]
    public void Pocket_And_VCarve_Are_Blocked_Too()
    {
        Assert.NotNull(CutPanel.AreaStrategyBlocker("pocket", "Pocket", new[] { OpenPolyline() }));
        Assert.NotNull(CutPanel.AreaStrategyBlocker("vcarve", "V-Carve", new[] { OpenPolyline() }));
    }

    [Fact]
    public void A_Closed_Selection_Is_Not_Blocked()
    {
        Assert.Null(CutPanel.AreaStrategyBlocker("profile", "Profile", new[] { ClosedRect() }));
        Assert.Null(CutPanel.AreaStrategyBlocker("pocket", "Pocket", new[] { ClosedRect() }));
    }

    [Fact]
    public void A_Mixed_Selection_Is_Not_Blocked()
    {
        // One closed shape means there IS something to cut; only an all-open selection
        // is refused.
        Assert.Null(CutPanel.AreaStrategyBlocker("profile", "Profile",
            new[] { OpenPolyline(), ClosedRect() }));
    }

    [Fact]
    public void Circles_And_Rectangles_Count_As_Closed()
    {
        // Both are implicitly closed even when Closed is not set.
        Assert.Null(CutPanel.AreaStrategyBlocker("pocket", "Pocket",
            new[] { VectorShape.Circle(new VectorPoint(0, 0), 20) }));
        Assert.Null(CutPanel.AreaStrategyBlocker("pocket", "Pocket", new[] { ClosedRect() }));
    }

    [Fact]
    public void An_Engraving_Strategy_Is_Allowed_On_Open_Paths()
    {
        // Engraving/drag-knife legitimately follow an open path — the guard must not
        // block every strategy.
        Assert.Null(CutPanel.AreaStrategyBlocker("quick-engrave", "Quick Engrave",
            new[] { OpenPolyline() }));
        Assert.Null(CutPanel.AreaStrategyBlocker("drag-knife", "Drag Knife",
            new[] { OpenPolyline() }));
    }

    [Fact]
    public void An_Empty_Selection_Is_Not_Blocked_By_This_Rule()
    {
        // "Nothing selected" is a different message, handled elsewhere.
        Assert.Null(CutPanel.AreaStrategyBlocker("profile", "Profile", Array.Empty<VectorShape>()));
    }

    [Fact]
    public void A_Closed_Shape_Still_Calculates_Normally()
    {
        var entry = Reg.Find("profile")!;
        var result = entry.Compute(new[] { ClosedRect() }, null, entry.DefaultsJson);

        Assert.NotEmpty(result.Gcode);
        Assert.Contains(result.Gcode, l => l.StartsWith("G1"));
    }

    [Fact]
    public void A_Pocket_Of_A_Closed_Shape_Still_Calculates()
    {
        var entry = Reg.Find("pocket")!;
        var result = entry.Compute(new[] { ClosedRect() }, null, entry.DefaultsJson);

        Assert.NotEmpty(result.Gcode);
    }

    [Fact]
    public void An_Empty_Layer_Has_Nothing_To_Report()
    {
        Assert.Empty(VectorValidator.Validate(Array.Empty<VectorShape>()));
    }
}
