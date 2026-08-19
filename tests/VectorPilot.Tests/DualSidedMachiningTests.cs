using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Two-sided machining. The mirror transform must be an involution — flipping twice
/// returns the original — or back-face cuts land mirrored and ruin the part.
/// </summary>
public class DualSidedMachiningTests
{
    private const double W = 200, H = 100, T = 18;

    [Fact]
    public void Top_Side_Is_Identity()
    {
        var p = new VectorPoint(30, 40);
        var m = DualSidedMachining.MapPoint(p, StockSide.Top, FlipAxis.Vertical, W, H);
        Assert.Equal(30, m.X, 6);
        Assert.Equal(40, m.Y, 6);
    }

    [Fact]
    public void Vertical_Flip_Mirrors_X_Only()
    {
        var m = DualSidedMachining.MapPoint(new VectorPoint(30, 40), StockSide.Bottom, FlipAxis.Vertical, W, H);
        Assert.Equal(170, m.X, 6);   // 200 - 30
        Assert.Equal(40, m.Y, 6);
    }

    [Fact]
    public void Horizontal_Flip_Mirrors_Y_Only()
    {
        var m = DualSidedMachining.MapPoint(new VectorPoint(30, 40), StockSide.Bottom, FlipAxis.Horizontal, W, H);
        Assert.Equal(30, m.X, 6);
        Assert.Equal(60, m.Y, 6);    // 100 - 40
    }

    [Fact]
    public void Flipping_Twice_Returns_The_Original()
    {
        foreach (var axis in new[] { FlipAxis.Vertical, FlipAxis.Horizontal })
        {
            var p = new VectorPoint(37.5, 12.25);
            var once = DualSidedMachining.MapPoint(p, StockSide.Bottom, axis, W, H);
            var twice = DualSidedMachining.MapPoint(once, StockSide.Bottom, axis, W, H);

            Assert.Equal(p.X, twice.X, 6);
            Assert.Equal(p.Y, twice.Y, 6);
        }
    }

    [Fact]
    public void The_Stock_Centre_Is_Fixed_By_The_Flip()
    {
        var centre = new VectorPoint(W / 2, H / 2);
        var m = DualSidedMachining.MapPoint(centre, StockSide.Bottom, FlipAxis.Vertical, W, H);
        Assert.Equal(centre.X, m.X, 6);
        Assert.Equal(centre.Y, m.Y, 6);
    }

    [Fact]
    public void Mapping_A_Shape_Maps_Every_Point()
    {
        var s = VectorShape.Rectangle(10, 10, 40, 20);
        var m = DualSidedMachining.MapShape(s, StockSide.Bottom, FlipAxis.Vertical, W, H);

        Assert.Equal(s.Points.Count, m.Points.Count);
        Assert.Equal(s.Closed, m.Closed);
        for (int i = 0; i < s.Points.Count; i++)
            Assert.Equal(W - s.Points[i].X, m.Points[i].X, 6);
    }

    [Fact]
    public void Depth_Is_Always_Negative()
    {
        Assert.Equal(-5, DualSidedMachining.MapDepth(5), 6);
        Assert.Equal(-5, DualSidedMachining.MapDepth(-5), 6);
    }

    [Fact]
    public void Web_Thickness_Is_What_Remains_Between_The_Cuts()
    {
        // 18mm stock, 6mm from the top, 6mm from the bottom → 6mm web.
        Assert.Equal(6, DualSidedMachining.WebThickness(T, -6, -6), 6);
        Assert.False(DualSidedMachining.CutsThrough(T, -6, -6));
    }

    [Fact]
    public void Meeting_Cuts_Are_Reported_As_Through()
    {
        Assert.Equal(0, DualSidedMachining.WebThickness(T, -9, -9), 6);
        Assert.True(DualSidedMachining.CutsThrough(T, -9, -9));
    }

    [Fact]
    public void Overlapping_Cuts_Give_A_Negative_Web()
    {
        Assert.True(DualSidedMachining.WebThickness(T, -12, -12) < 0);
        Assert.True(DualSidedMachining.CutsThrough(T, -12, -12));
    }

    [Fact]
    public void Registration_Holes_Survive_The_Flip()
    {
        var holes = DualSidedMachining.RegistrationHoles(W, H, FlipAxis.Vertical, margin: 12);
        Assert.Equal(4, holes.Count);

        // Every hole must map onto another hole, or they are useless as datums.
        foreach (var h in holes)
        {
            var m = DualSidedMachining.MapPoint(h, StockSide.Bottom, FlipAxis.Vertical, W, H);
            Assert.Contains(holes, other =>
                Math.Abs(other.X - m.X) < 1e-6 && Math.Abs(other.Y - m.Y) < 1e-6);
        }
    }

    [Fact]
    public void No_Registration_Holes_On_Undersized_Stock()
    {
        Assert.Empty(DualSidedMachining.RegistrationHoles(10, 100, FlipAxis.Vertical, margin: 12));
        Assert.Empty(DualSidedMachining.RegistrationHoles(200, 8, FlipAxis.Vertical, margin: 12));
    }

    [Fact]
    public void Flip_Instructions_Pause_And_Stop_The_Spindle()
    {
        var lines = DualSidedMachining.FlipInstructions(FlipAxis.Vertical, T);

        Assert.Contains(lines, l => l.Contains("M5"));   // spindle off before handling
        Assert.Contains(lines, l => l.Contains("M0"));   // pause for the operator
        Assert.Contains(lines, l => l.Contains("Re-zero Z"));
        Assert.Contains(lines, l => l.Contains("18"));   // states the thickness
    }

    [Fact]
    public void Flip_Instructions_Name_The_Direction()
    {
        Assert.Contains(DualSidedMachining.FlipInstructions(FlipAxis.Vertical, T),
            l => l.Contains("left-to-right"));
        Assert.Contains(DualSidedMachining.FlipInstructions(FlipAxis.Horizontal, T),
            l => l.Contains("front-to-back"));
    }
}
