using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>H-202: scallop-height driven stepover for the 3D finish pass.</summary>
public class ScallopFinishTests
{
    private const int Width = 40;
    private const int Height = 30;
    private const double CellSizeMm = 1.0;

    private static HeightfieldData MakeField()
    {
        var heights = new double[Width * Height];
        for (int row = 0; row < Height; row++)
        {
            for (int col = 0; col < Width; col++)
            {
                // A gentle dome-ish surface so Z actually varies along and across the rows.
                double u = (col - (Width - 1) / 2.0) / Width;
                double v = (row - (Height - 1) / 2.0) / Height;
                heights[row * Width + col] = 3.0 + 2.0 * Math.Cos(u * 3.0) * Math.Cos(v * 3.0);
            }
        }
        return new HeightfieldData(Width, Height, CellSizeMm, 0.0, 0.0, heights);
    }

    private static HeightfieldFinishParams BaseParams() => new()
    {
        ToolDiameterMm = 3.175,
        StepOverMm = 0.8,
        FeedRateMmPerMin = 1000,
        PlungeFeedRateMmPerMin = 300,
        SafeZHeightMm = 5.0
    };

    private static List<string> Run(double scallopHeightMm)
    {
        var p = BaseParams();
        if (scallopHeightMm > 0) p.ScallopHeightMm = scallopHeightMm;
        return HeightfieldFinishEngine.Compute(MakeField(), p).GcodeLines;
    }

    private static int CountG1(IEnumerable<string> lines)
        => lines.Count(l => l.StartsWith("G1", StringComparison.Ordinal));

    [Fact]
    public void SmallerScallopProducesMoreG1Moves()
    {
        int fine = CountG1(Run(0.05));
        int coarse = CountG1(Run(0.4));
        Assert.True(fine > coarse, $"expected finer scallop to cut more: {fine} vs {coarse}");
    }

    [Fact]
    public void TwoDifferentScallopsAreNotTheSameProgram()
    {
        Assert.NotEqual(Run(0.05), Run(0.4));
    }

    [Fact]
    public void ZeroScallopIsIdenticalToNotSettingIt()
    {
        var baseline = HeightfieldFinishEngine.Compute(MakeField(), BaseParams()).GcodeLines;
        var explicitZero = Run(0.0);
        var zeroSet = BaseParams();
        zeroSet.ScallopHeightMm = 0;
        var zeroProgram = HeightfieldFinishEngine.Compute(MakeField(), zeroSet).GcodeLines;

        Assert.Equal(baseline, explicitZero);
        Assert.Equal(baseline, zeroProgram);
    }

    [Theory]
    [InlineData(0.0, 0.05)]
    [InlineData(-3.175, 0.05)]
    [InlineData(3.175, 0.0)]
    [InlineData(3.175, -0.05)]
    public void UnusableInputsReturnZero(double toolDiameterMm, double scallopHeightMm)
    {
        Assert.Equal(0, HeightfieldFinishEngine.StepOverForScallop(toolDiameterMm, scallopHeightMm));
    }

    [Fact]
    public void ScallopAtOrAboveRadiusReturnsZero()
    {
        Assert.Equal(0, HeightfieldFinishEngine.StepOverForScallop(3.175, 3.175 / 2.0));
        Assert.Equal(0, HeightfieldFinishEngine.StepOverForScallop(3.175, 2.0));
    }

    [Fact]
    public void NaNAndInfinityReturnZero()
    {
        Assert.Equal(0, HeightfieldFinishEngine.StepOverForScallop(double.NaN, 0.05));
        Assert.Equal(0, HeightfieldFinishEngine.StepOverForScallop(3.175, double.NaN));
        Assert.Equal(0, HeightfieldFinishEngine.StepOverForScallop(double.PositiveInfinity, 0.05));
        Assert.Equal(0, HeightfieldFinishEngine.StepOverForScallop(3.175, double.PositiveInfinity));
    }

    [Fact]
    public void SmallerScallopGivesSmallerStepOver()
    {
        double fine = HeightfieldFinishEngine.StepOverForScallop(3.175, 0.05);
        double coarse = HeightfieldFinishEngine.StepOverForScallop(3.175, 0.4);
        Assert.True(fine > 0);
        Assert.True(fine < coarse, $"expected {fine} < {coarse}");
    }

    [Fact]
    public void StepOverNeverExceedsToolDiameter()
    {
        foreach (double h in new[] { 0.001, 0.05, 0.2, 0.4, 1.0, 1.5 })
        {
            double s = HeightfieldFinishEngine.StepOverForScallop(3.175, h);
            Assert.True(s <= 3.175 + 1e-9, $"scallop {h} gave stepover {s}");
        }
    }

    [Fact]
    public void KnownGeometryMatchesClosedForm()
    {
        const double dia = 6.0;
        const double h = 0.5;
        double r = dia / 2.0;
        double expected = 2.0 * Math.Sqrt(r * r - (r - h) * (r - h));
        Assert.Equal(expected, HeightfieldFinishEngine.StepOverForScallop(dia, h), 9);
    }

    [Fact]
    public void NoNaNInEmittedGcode()
    {
        foreach (double scallop in new[] { 0.0, 0.05, 0.4 })
        {
            foreach (string line in Run(scallop))
            {
                Assert.DoesNotContain("NaN", line, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
