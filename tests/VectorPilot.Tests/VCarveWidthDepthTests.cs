using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// V-carve depth must come from the LOCAL CHANNEL WIDTH, not from Y position on
/// the page. The previous engine used z = actualZ * (0.3 + 0.7 * normalizedY),
/// so an identical shape carved differently depending on where it sat — and the
/// goldens encoded that. These tests pin the real relationship.
/// </summary>
public class VCarveWidthDepthTests
{
    [Fact]
    public void Depth_Is_HalfWidth_Over_Tan_HalfAngle()
    {
        // 90° bit: halfAngle 45°, tan = 1 → depth == halfWidth.
        Assert.Equal(-3.0, VCarveGeometry.DepthForHalfWidth(3.0, 90, maxDepth: 100), 6);

        // 60° bit: halfAngle 30°, tan ≈ 0.5774 → depth ≈ 3 / 0.5774 ≈ 5.196.
        Assert.Equal(-5.196, VCarveGeometry.DepthForHalfWidth(3.0, 60, maxDepth: 100), 3);
    }

    [Fact]
    public void Wider_Channels_Carve_Deeper()
    {
        double narrow = VCarveGeometry.DepthForHalfWidth(1.0, 90, 100);
        double wide = VCarveGeometry.DepthForHalfWidth(5.0, 90, 100);
        Assert.True(wide < narrow, $"wide {wide} must be deeper (more negative) than narrow {narrow}");
    }

    [Fact]
    public void Depth_Is_Clamped_To_MaxDepth()
    {
        // A very wide channel would admit 50mm; the tool is limited to 6.
        Assert.Equal(-6.0, VCarveGeometry.DepthForHalfWidth(50.0, 90, maxDepth: 6), 6);
    }

    [Fact]
    public void Zero_Width_Carves_Nothing()
    {
        Assert.Equal(0.0, VCarveGeometry.DepthForHalfWidth(0, 90, 10), 6);
        Assert.Equal(0.0, VCarveGeometry.DepthForHalfWidth(-1, 90, 10), 6);
    }

    [Fact]
    public void Sharper_Bits_Reach_Deeper_In_The_Same_Channel()
    {
        double blunt = VCarveGeometry.DepthForHalfWidth(2.0, 120, 100);   // wide included angle
        double sharp = VCarveGeometry.DepthForHalfWidth(2.0, 30, 100);    // narrow included angle
        Assert.True(sharp < blunt, $"sharp bit {sharp} must reach deeper than blunt {blunt}");
    }

    [Fact]
    public void Channel_Width_Comes_From_The_Opposing_Edge()
    {
        // Two parallel lines 6mm apart: half-width at any point on one is 3mm.
        var a = new VectorShape { Type = ShapeType.Polyline };
        a.Points.AddRange(new[] { new VectorPoint(0, 0), new VectorPoint(20, 0) });
        var b = new VectorShape { Type = ShapeType.Polyline };
        b.Points.AddRange(new[] { new VectorPoint(0, 6), new VectorPoint(20, 6) });

        var all = new[] { a, b };
        double halfWidth = VCarveGeometry.DistanceToNearestOtherEdge(a, 1, all);
        Assert.Equal(6.0, halfWidth, 6);   // distance to the opposing edge
    }

    [Fact]
    public void Own_Adjacent_Segments_Are_Ignored()
    {
        // A lone open path has no opposing edge: its own neighbours must not
        // register as zero-width, which would force depth to 0 everywhere.
        var only = new VectorShape { Type = ShapeType.Polyline };
        only.Points.AddRange(new[]
        {
            new VectorPoint(0, 0), new VectorPoint(10, 0), new VectorPoint(20, 0)
        });

        // Point 1's neighbours are skipped, so the nearest "other" edge is none.
        double w = VCarveGeometry.DistanceToNearestOtherEdge(only, 1, new[] { only });
        Assert.Equal(0.0, w, 6);
    }

    [Fact]
    public void Narrow_Gap_Carves_Shallower_Than_A_Wide_Gap()
    {
        VectorShape Pair(double gap, out VectorShape opposite)
        {
            var lo = new VectorShape { Type = ShapeType.Polyline };
            lo.Points.AddRange(new[] { new VectorPoint(0, 0), new VectorPoint(20, 0) });
            opposite = new VectorShape { Type = ShapeType.Polyline };
            opposite.Points.AddRange(new[] { new VectorPoint(0, gap), new VectorPoint(20, gap) });
            return lo;
        }

        var tight = Pair(2, out var tightOpp);
        var loose = Pair(12, out var looseOpp);

        double dTight = VCarveGeometry.DepthForHalfWidth(
            VCarveGeometry.DistanceToNearestOtherEdge(tight, 1, new[] { tight, tightOpp }), 90, 100);
        double dLoose = VCarveGeometry.DepthForHalfWidth(
            VCarveGeometry.DistanceToNearestOtherEdge(loose, 1, new[] { loose, looseOpp }), 90, 100);

        Assert.True(dLoose < dTight,
            $"wide gap {dLoose} must carve deeper than narrow gap {dTight}");
    }
}
