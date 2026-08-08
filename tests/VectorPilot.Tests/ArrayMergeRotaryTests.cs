using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Ported engine tests for ArrayCopyEngine, MergedToolpathEngine and RotaryWrapEngine
/// (Swift: ArrayCopyToolpath.swift, ArrayCopyAndMerge.swift, MergedToolpath.swift,
/// RotaryWrapToolpath.swift).
/// </summary>
public class ArrayMergeRotaryTests
{
    private static VectorShape Line(double x0, double y0, double x1, double y1)
        => VectorShape.Line(new VectorPoint(x0, y0), new VectorPoint(x1, y1));

    private static int CountMotionLines(IReadOnlyList<string> lines)
        => lines.Count(l => l.StartsWith("G0 ") || l.StartsWith("G1 ") || l.StartsWith("G2 ") || l.StartsWith("G3 "));

    // ------------------------------------------------------------------ Array copy

    [Fact]
    public void ArrayCopy_Grid2x3_Of_4LinePath_Yields_24_MotionLines()
    {
        var baseGcode = new[]
        {
            "(comment)",
            "G1 X1.000 Y0.000",
            "G1 X1.000 Y1.000",
            "G1 X0.000 Y1.000",
            "G1 X0.000 Y0.000",
            "M30"
        };

        var result = ArrayCopyEngine.ComputeGrid(baseGcode, new GridPattern(2, 3, 10.0, 10.0));

        Assert.True(result.Success);
        Assert.Equal(6, result.TotalCount);
        Assert.Equal(5, result.CopiedIds.Count);
        // 6 cells × 4 motion lines = 24
        Assert.Equal(24, CountMotionLines(result.GcodeLines));
        // preamble/postamble emitted once, not per copy
        Assert.Equal(1, result.GcodeLines.Count(l => l == "(comment)"));
        Assert.Equal(1, result.GcodeLines.Count(l => l == "M30"));
        // offset correctness: cell (row 2, col 1) → +10 X, +20 Y
        Assert.Contains(result.GcodeLines, l => l == "G1 X10.000 Y20.000");
        // original cell keeps coordinates
        Assert.Contains(result.GcodeLines, l => l == "G1 X1.000 Y0.000");
    }

    [Fact]
    public void ArrayCopy_Linear_Offsets_Along_Angle()
    {
        var baseGcode = new[] { "G1 X0.000 Y0.000" };

        var result = ArrayCopyEngine.ComputeLinear(baseGcode, new LinearPattern(3, 5.0, 0.0));

        Assert.True(result.Success);
        Assert.Equal(3, result.TotalCount);
        Assert.Equal(3, CountMotionLines(result.GcodeLines));
        Assert.Contains(result.GcodeLines, l => l == "G1 X5.000 Y0.000");
        Assert.Contains(result.GcodeLines, l => l == "G1 X10.000 Y0.000");
    }

    [Fact]
    public void ArrayCopy_Linear_Angle90_Offsets_Along_Y()
    {
        var result = ArrayCopyEngine.ComputeLinear(new[] { "G1 X0.000 Y0.000" }, new LinearPattern(3, 5.0, 90.0));

        Assert.Contains(result.GcodeLines, l => l == "G1 X0.000 Y5.000");
        Assert.Contains(result.GcodeLines, l => l == "G1 X0.000 Y10.000");
    }

    [Fact]
    public void ArrayCopy_Linear_Count1_Is_Single()
    {
        var result = ArrayCopyEngine.ComputeLinear(new[] { "G1 X0.000 Y0.000" }, new LinearPattern(1));

        Assert.True(result.Success);
        Assert.Equal(1, result.TotalCount);
        Assert.Empty(result.CopiedIds);
        Assert.Equal(1, CountMotionLines(result.GcodeLines));
    }

    [Fact]
    public void ArrayCopy_Linear_InvalidCount_Fails()
    {
        // The ctor clamps (mirroring Swift's init), so set the property directly to hit the
        // engine's validation (which mirrors Swift's createLinearArray check).
        var result = ArrayCopyEngine.ComputeLinear(new[] { "G1 X0.000 Y0.000" }, new LinearPattern { Count = 0 });

        Assert.False(result.Success);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal("Count must be at least 1", result.ErrorMessage);
    }

    [Fact]
    public void ArrayCopy_Grid_InvalidDimensions_Fails()
    {
        var result = ArrayCopyEngine.ComputeGrid(new[] { "G1 X0.000 Y0.000" }, new GridPattern(0, 2));

        Assert.False(result.Success);
        Assert.Equal("Columns and rows must be at least 1", result.ErrorMessage);
    }

    [Fact]
    public void ArrayCopy_Circular_Rotates_Around_Center()
    {
        var baseGcode = new[] { "G1 X10.000 Y0.000" };
        var pattern = new CircularPattern(4, centerX: 0, centerY: 0, startAngleDeg: 0, endAngleDeg: 360, radiusMm: 10);

        var result = ArrayCopyEngine.ComputeCircular(baseGcode, pattern);

        Assert.True(result.Success);
        Assert.Equal(4, CountMotionLines(result.GcodeLines));
        // 120° rotation of (10, 0) about the origin → (-5, 8.660254…)
        Assert.Contains(result.GcodeLines, l => l == "G1 X-5.000 Y8.660");
        // 240° rotation → (-5, -8.660254…)
        Assert.Contains(result.GcodeLines, l => l == "G1 X-5.000 Y-8.660");
    }

    [Fact]
    public void ArrayCopy_Circular_InvalidRadius_Fails()
    {
        var result = ArrayCopyEngine.ComputeCircular(new[] { "G1 X10.000 Y0.000" },
            new CircularPattern(4, radiusMm: 0));

        Assert.False(result.Success);
        Assert.Equal("Radius must be positive", result.ErrorMessage);
    }

    [Fact]
    public void ArrayCopy_RotaryAxis_Offsets_A()
    {
        var result = ArrayCopyEngine.ComputeLinear(new[] { "G1 A10.000" }, new LinearPattern(2, 90.0, rotaryAxis: true));

        Assert.True(result.Success);
        Assert.Contains(result.GcodeLines, l => l == "G1 A10.000");
        Assert.Contains(result.GcodeLines, l => l == "G1 A100.000");
    }

    [Fact]
    public void ArrayCopy_Formatter_Is_Invariant()
    {
        // Values are emitted with '.' decimals regardless of ambient culture.
        var result = ArrayCopyEngine.ComputeLinear(new[] { "G1 X0.000 Y0.000" }, new LinearPattern(2, 10.5, 0.0));
        Assert.Contains(result.GcodeLines, l => l == "G1 X10.500 Y0.000");
    }

    // ------------------------------------------------------------------ Merged toolpath

    private static MergeSourceGcode Source(string name, int tool, params string[] lines)
        => new() { Name = name, ToolNumber = tool, GcodeLines = lines };

    [Fact]
    public void Merged_TwoPaths_Inserts_ToolChange_Between()
    {
        var path1 = Source("path1", 1,
            "G1 X0.000 Y0.000",
            "G1 X10.000 Y0.000");
        var path2 = Source("path2", 2,
            "G1 X0.000 Y10.000",
            "G1 X10.000 Y10.000");

        var result = MergedToolpathEngine.Compute(new[] { path1, path2 });

        Assert.True(result.Success);
        var g = result.GcodeLines;
        // exactly one tool-change line, sitting between the two blocks
        Assert.Equal(1, g.Count(l => l.Contains("M6")));
        int toolChange = g.FindIndex(l => l.Contains("M6"));
        int path1LastMove = g.FindIndex(l => l == "G1 X10.000 Y0.000");
        int path2FirstMove = g.FindIndex(l => l == "G1 X0.000 Y10.000");
        Assert.True(path1LastMove >= 0 && path2FirstMove >= 0);
        Assert.True(toolChange > path1LastMove && toolChange < path2FirstMove,
            "tool change must sit between the two paths' motion blocks");
        Assert.Contains(g, l => l == "T2 M6");
        Assert.Equal(4, result.TotalSegments);
        Assert.Equal(20.0, result.TotalLengthMm, 6);
    }

    [Fact]
    public void Merged_SameTool_NoToolChange()
    {
        var path1 = Source("a", 1, "G1 X0.000 Y0.000");
        var path2 = Source("b", 1, "G1 X0.000 Y5.000");

        var result = MergedToolpathEngine.Compute(new[] { path1, path2 });

        Assert.True(result.Success);
        Assert.Equal(0, result.GcodeLines.Count(l => l.Contains("M6")));
    }

    [Fact]
    public void Merged_LessThanTwoSources_Fails()
    {
        var result = MergedToolpathEngine.Compute(new[] { Source("only", 1, "G1 X0.000 Y0.000") });

        Assert.False(result.Success);
        Assert.Equal("Need at least 2 toolpaths to merge", result.ErrorMessage);
        Assert.Equal(0, result.TotalSegments);
        Assert.Equal(0.0, result.TotalLengthMm, 6);
    }

    [Fact]
    public void Merged_LeftToRight_Orders_By_X()
    {
        var right = Source("right", 1, "G1 X50.000 Y0.000");
        var left = Source("left", 2, "G1 X0.000 Y0.000");

        var result = MergedToolpathEngine.Compute(new[] { right, left }, MergeOrderStrategy.LeftToRight);

        int leftIdx = result.GcodeLines.FindIndex(l => l.Contains("X0.000"));
        int rightIdx = result.GcodeLines.FindIndex(l => l.Contains("X50.000"));
        Assert.True(leftIdx >= 0 && rightIdx >= 0 && leftIdx < rightIdx,
            "leftmost path should be emitted first");
    }

    // ------------------------------------------------------------------ Rotary wrap

    [Fact]
    public void RotaryWrap_XAxisLine_Emits_A_Axis()
    {
        var result = RotaryWrapEngine.Compute(new[] { Line(0, 0, 10, 0) }, new RotaryWrapParams());

        var g = result.GcodeLines;
        Assert.Contains("%", g);
        Assert.Contains(g, l => l.Contains("O=ROTARY_WRAP_TOOLPATH"));
        Assert.Contains(g, l => l.Contains("(Rotary wrap: Ø 50.0mm · depth 1.00mm · CW)"));
        Assert.Contains(g, l => l.Contains("M30"));
        // X=0 → A=0.000; X=10 on Ø50 (circumference π·50) → 10/(π·50)·360 = 22.918…°
        Assert.Contains(g, l => l == "G0 A0.000 Y0.000");
        Assert.Contains(g, l => l == "G0 Z5.000");
        Assert.Contains(g, l => l == "G1 Z-1.000 F300");
        Assert.Contains(g, l => l == "G1 A22.918 Y0.000 F1200");
        Assert.Equal(1, result.FeatureCount);
        // time = 10mm / 1200 mm/min · 60 + 1 feature · 1.2 s = 1.7 s
        Assert.Equal(1.7, result.EstimatedTimeSeconds, 6);
    }

    [Fact]
    public void RotaryWrap_CounterClockwise_Reflects_Angle()
    {
        var p = new RotaryWrapParams { Direction = RotaryDirection.CounterClockwise };
        var result = RotaryWrapEngine.Compute(new[] { Line(0, 0, 10, 0) }, p);

        Assert.Contains(result.GcodeLines, l => l.Contains("(Rotary wrap: Ø 50.0mm · depth 1.00mm · CCW)"));
        // 360 − 22.918… = 337.082°
        Assert.Contains(result.GcodeLines, l => l == "G1 A337.082 Y0.000 F1200");
    }

    [Fact]
    public void RotaryWrap_YAxisLine_Keeps_Axis_Dimension()
    {
        var result = RotaryWrapEngine.Compute(new[] { Line(0, 0, 0, 10) }, new RotaryWrapParams());

        // X stays 0 → A stays 0; Y carries the axis dimension.
        Assert.Contains(result.GcodeLines, l => l == "G0 A0.000 Y0.000");
        Assert.Contains(result.GcodeLines, l => l == "G1 A0.000 Y10.000 F1200");
    }

    [Fact]
    public void RotaryWrap_Spindle_On_When_Rpm_Set()
    {
        var p = new RotaryWrapParams { SpindleRpm = 12000 };
        var result = RotaryWrapEngine.Compute(new[] { Line(0, 0, 10, 0) }, p);

        Assert.Contains(result.GcodeLines, l => l == "M3 S12000");
    }

    [Fact]
    public void RotaryWrap_LinearToAngular_Matches_Swift_Math()
    {
        var config = new RotaryConfig(RotaryMode.Cylinder, 50.0);
        // circumference = π·diameter ≈ 157.08 mm
        Assert.Equal(Math.PI * 50.0, RotaryWrapEngine.Circumference(config), 9);
        // angle = x/circumference·360, wrapped to 0..360
        Assert.Equal(0.0, RotaryWrapEngine.LinearToAngular(0, config), 9);
        // half a circumference → 180°; a full circumference wraps back to 0
        Assert.Equal(180.0, RotaryWrapEngine.LinearToAngular(RotaryWrapEngine.Circumference(config) / 2, config), 9);
        Assert.Equal(0.0, RotaryWrapEngine.LinearToAngular(RotaryWrapEngine.Circumference(config), config), 9);
        // one full extra wrap lands back at 0
        Assert.Equal(0.0, RotaryWrapEngine.LinearToAngular(2 * RotaryWrapEngine.Circumference(config), config), 9);
        // negative linear positions wrap positive
        Assert.Equal(180.0, RotaryWrapEngine.LinearToAngular(-RotaryWrapEngine.Circumference(config) / 2, config), 9);
    }
}
