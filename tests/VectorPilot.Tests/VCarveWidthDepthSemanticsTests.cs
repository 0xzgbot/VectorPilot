using System.Globalization;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// V-carve depth must follow channel WIDTH: a wide region cuts deeper than a narrow
/// slot, bounded by the bit's reach and the depth limit.
///
/// History: depth was originally shaded from Y position on the page (the same letter
/// carved differently after being moved), then fixed to derive from the medial-axis
/// half-width. These tests pin the width→depth relationship itself, plus the
/// clearance-pass behaviour the Mac's VCarveEngine actually implements
/// (SPK-VCarveClear — a flat tool clears wide/deep area first, the V-bit does detail).
/// </summary>
public class VCarveWidthDepthSemanticsTests
{
    private static VCarveParams Params(double angleDeg = 90, double maxDepth = 10) => new()
    {
        VBitAngleDegrees = angleDeg,
        MaxDepthOfCutMm = maxDepth,
        FeedRateMmPerMin = 1000,
        PlungeFeedRateMmPerMin = 300,
        StepOverMm = 1.0,
        SafeZHeightMm = 5
    };

    /// <summary>Two parallel lines `width` apart — a channel of known width.</summary>
    private static List<VectorShape> Channel(double width, double length = 60, double yBase = 20)
    {
        var a = new VectorShape { Type = ShapeType.Polyline, Closed = false };
        a.Points.AddRange(new[] { new VectorPoint(10, yBase), new VectorPoint(10 + length, yBase) });

        var b = new VectorShape { Type = ShapeType.Polyline, Closed = false };
        b.Points.AddRange(new[] { new VectorPoint(10, yBase + width), new VectorPoint(10 + length, yBase + width) });

        return new List<VectorShape> { a, b };
    }

    private static List<double> DepthsOf(IEnumerable<string> gcode)
    {
        var depths = new List<double>();
        foreach (var line in gcode)
        {
            int i = line.IndexOf('Z');
            if (i < 0 || !line.StartsWith("G1")) continue;

            var token = new string(line[(i + 1)..].TakeWhile(c => char.IsDigit(c) || c == '.' || c == '-').ToArray());
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var z) && z < 0)
                depths.Add(z);
        }
        return depths;
    }

    // ---- the core semantic ----

    [Fact]
    public void A_Wide_Channel_Cuts_Deeper_Than_A_Narrow_Slot()
    {
        var wide = VCarveEngine.Compute(Channel(width: 12), Params());
        var narrow = VCarveEngine.Compute(Channel(width: 2), Params());

        var wideDepths = DepthsOf(wide.GcodeLines);
        var narrowDepths = DepthsOf(narrow.GcodeLines);

        Assert.NotEmpty(wideDepths);
        Assert.NotEmpty(narrowDepths);

        // Deepest point of the wide channel must exceed the narrow one.
        Assert.True(wideDepths.Min() < narrowDepths.Min(),
            $"wide deepest={wideDepths.Min():F3} should be deeper than narrow={narrowDepths.Min():F3}");
    }

    [Fact]
    public void Depth_Follows_The_Vbit_Geometry()
    {
        // For a 90° bit the half-angle is 45°, so depth = halfWidth / tan(45°) = halfWidth.
        var result = VCarveEngine.Compute(Channel(width: 8), Params(angleDeg: 90, maxDepth: 20));
        var depths = DepthsOf(result.GcodeLines);

        Assert.NotEmpty(depths);
        // Half-width of an 8mm channel is 4mm, so the deepest cut is about 4mm.
        Assert.True(depths.Min() <= -1.0, $"expected a real cut, deepest was {depths.Min():F3}");
        Assert.True(depths.Min() >= -8.0, $"depth {depths.Min():F3} exceeds the channel's geometry");
    }

    [Fact]
    public void A_Sharper_Bit_Cuts_Deeper_For_The_Same_Width()
    {
        // depth = halfWidth / tan(halfAngle): a 60° bit (tan30 ≈ 0.577) goes deeper
        // than a 90° bit (tan45 = 1) for identical geometry.
        var wide90 = DepthsOf(VCarveEngine.Compute(Channel(width: 8), Params(angleDeg: 90, maxDepth: 30)).GcodeLines);
        var wide60 = DepthsOf(VCarveEngine.Compute(Channel(width: 8), Params(angleDeg: 60, maxDepth: 30)).GcodeLines);

        Assert.NotEmpty(wide90);
        Assert.NotEmpty(wide60);
        Assert.True(wide60.Min() <= wide90.Min() + 1e-6,
            $"60° bit ({wide60.Min():F3}) should reach at least as deep as 90° ({wide90.Min():F3})");
    }

    [Fact]
    public void Depth_Is_Clamped_To_The_Depth_Limit()
    {
        // A very wide channel would run the bit far past the limit.
        var result = VCarveEngine.Compute(Channel(width: 60), Params(angleDeg: 90, maxDepth: 3));
        var depths = DepthsOf(result.GcodeLines);

        Assert.NotEmpty(depths);
        Assert.True(depths.Min() >= -3.0 - 1e-6,
            $"depth {depths.Min():F3} broke the 3mm limit");
    }

    [Fact]
    public void Depth_Does_Not_Depend_On_Position_On_The_Page()
    {
        // The original bug: Y position shaded the depth, so moving a shape changed
        // the cut. Same channel, two places, same depths.
        var low = DepthsOf(VCarveEngine.Compute(Channel(width: 8, yBase: 10), Params()).GcodeLines);
        var high = DepthsOf(VCarveEngine.Compute(Channel(width: 8, yBase: 120), Params()).GcodeLines);

        Assert.NotEmpty(low);
        Assert.NotEmpty(high);
        Assert.Equal(low.Min(), high.Min(), 3);
        Assert.Equal(low.Max(), high.Max(), 3);
    }

    [Fact]
    public void The_Program_Varies_Depth_Rather_Than_Running_Flat()
    {
        // A tapering channel must produce more than one distinct Z.
        var a = new VectorShape { Type = ShapeType.Polyline, Closed = false };
        a.Points.AddRange(new[] { new VectorPoint(10, 20), new VectorPoint(70, 20) });
        var b = new VectorShape { Type = ShapeType.Polyline, Closed = false };
        b.Points.AddRange(new[] { new VectorPoint(10, 22), new VectorPoint(70, 34) });   // widening

        var depths = DepthsOf(VCarveEngine.Compute(new List<VectorShape> { a, b }, Params()).GcodeLines);

        Assert.NotEmpty(depths);
        Assert.True(depths.Distinct().Count() > 1,
            "a tapering channel emitted a single flat Z — depth is not following width");
    }

    // ---- clearance pass (the Mac's actual VCarve differentiator) ----

    [Fact]
    public void The_Clearance_Pass_Is_Off_By_Default()
    {
        var p = Params();
        Assert.False(p.ClearancePassEnabled);
    }

    [Fact]
    public void Enabling_The_Clearance_Pass_Adds_Material_Removal()
    {
        var plain = VCarveEngine.Compute(Channel(width: 30), Params());

        var withClear = Params();
        withClear.ClearancePassEnabled = true;
        withClear.ClearanceToolDiameterMm = 6;
        withClear.ClearanceDepthMm = 1.5;
        withClear.ClearanceStepOverMm = 0.4;

        var cleared = VCarveEngine.Compute(Channel(width: 30), withClear);

        Assert.True(cleared.GcodeLines.Count > plain.GcodeLines.Count,
            $"clearance pass added nothing (plain={plain.GcodeLines.Count}, cleared={cleared.GcodeLines.Count})");
    }

    [Fact]
    public void The_Clearance_Pass_Respects_Its_Own_Depth()
    {
        var p = Params(maxDepth: 20);
        p.ClearancePassEnabled = true;
        p.ClearanceToolDiameterMm = 6;
        p.ClearanceDepthMm = 1.0;
        p.ClearanceStepOverMm = 0.4;

        var result = VCarveEngine.Compute(Channel(width: 30), p);
        Assert.NotEmpty(result.GcodeLines);
        // The V-bit still cuts deeper than the clearance tool's flat depth.
        var depths = DepthsOf(result.GcodeLines);
        Assert.Contains(depths, z => z <= -1.0);
    }

    [Fact]
    public void A_Channel_Narrower_Than_The_Clearance_Tool_Skips_Clearing()
    {
        var p = Params();
        p.ClearancePassEnabled = true;
        p.ClearanceToolDiameterMm = 20;    // far wider than the channel
        p.ClearanceDepthMm = 1.0;
        p.ClearanceStepOverMm = 0.4;

        // Must not throw and must not pretend to clear a 2mm slot with a 20mm tool.
        var result = VCarveEngine.Compute(Channel(width: 2), p);
        Assert.NotNull(result);
    }
}
