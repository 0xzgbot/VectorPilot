using System;
using System.Linq;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class LithophaneEngineTests
{
    private static LithophaneParams Defaults() => new LithophaneParams();

    [Fact]
    public void DarkPixelIsThickerThanLightPixel()
    {
        var lum = new[] { 0.0, 1.0 };
        var field = LithophaneEngine.Compute(lum, 2, 1, Defaults());

        Assert.NotNull(field);
        Assert.True(field!.Heights[0] > field.Heights[1]);
    }

    [Fact]
    public void InvertReversesTheDirection()
    {
        var lum = new[] { 0.0, 1.0 };
        var p = Defaults();
        p.Invert = true;

        var field = LithophaneEngine.Compute(lum, 2, 1, p);

        Assert.NotNull(field);
        Assert.True(field!.Heights[0] < field.Heights[1]);
    }

    [Fact]
    public void AllCellsWithinConfiguredRange()
    {
        var lum = new[] { 0.0, 0.25, 0.5, 0.75, 1.0, 0.1 };
        var p = Defaults();

        var field = LithophaneEngine.Compute(lum, 3, 2, p);

        Assert.NotNull(field);
        Assert.All(field!.Heights, h =>
        {
            Assert.True(h >= p.MinThicknessMm);
            Assert.True(h <= p.MaxThicknessMm);
        });
    }

    [Fact]
    public void NoNaNEvenForBadInputLuminance()
    {
        var lum = new[] { double.NaN, -5.0, 42.0, 0.5 };

        var field = LithophaneEngine.Compute(lum, 2, 2, Defaults());

        Assert.NotNull(field);
        Assert.DoesNotContain(field!.Heights, double.IsNaN);
    }

    [Fact]
    public void FullyBlackImageIsUniformAtMaxThickness()
    {
        var lum = Enumerable.Repeat(0.0, 9).ToArray();
        var p = Defaults();

        var field = LithophaneEngine.Compute(lum, 3, 3, p);

        Assert.NotNull(field);
        Assert.All(field!.Heights, h => Assert.Equal(p.MaxThicknessMm, h, 9));
    }

    [Fact]
    public void FullyWhiteImageIsUniformAtMinThickness()
    {
        var lum = Enumerable.Repeat(1.0, 9).ToArray();
        var p = Defaults();

        var field = LithophaneEngine.Compute(lum, 3, 3, p);

        Assert.NotNull(field);
        Assert.All(field!.Heights, h => Assert.Equal(p.MinThicknessMm, h, 9));
    }

    [Fact]
    public void MidGreyLandsBetweenMinAndMax()
    {
        var lum = new[] { 0.5 };
        var p = Defaults();

        var field = LithophaneEngine.Compute(lum, 1, 1, p);

        Assert.NotNull(field);
        double h = field!.Heights[0];
        Assert.True(h > p.MinThicknessMm);
        Assert.True(h < p.MaxThicknessMm);
        Assert.Equal((p.MinThicknessMm + p.MaxThicknessMm) / 2.0, h, 9);
    }

    [Fact]
    public void DimensionsAndCellSizeArePreserved()
    {
        var lum = Enumerable.Repeat(0.5, 4 * 3).ToArray();
        var p = Defaults();
        p.CellSizeMm = 0.35;

        var field = LithophaneEngine.Compute(lum, 4, 3, p);

        Assert.NotNull(field);
        Assert.Equal(4, field!.Width);
        Assert.Equal(3, field.Height);
        Assert.Equal(0.35, field.CellSizeMm, 9);
        Assert.Equal(12, field.Heights.Length);
    }

    [Fact]
    public void NullReturnedForLengthMismatch()
    {
        var lum = new[] { 0.0, 1.0, 0.5 };

        Assert.Null(LithophaneEngine.Compute(lum, 2, 2, Defaults()));
    }

    [Fact]
    public void NullReturnedForNonPositiveDimensions()
    {
        var lum = new[] { 0.5 };

        Assert.Null(LithophaneEngine.Compute(lum, 0, 1, Defaults()));
        Assert.Null(LithophaneEngine.Compute(lum, 1, 0, Defaults()));
        Assert.Null(LithophaneEngine.Compute(lum, -3, 2, Defaults()));
    }

    [Fact]
    public void MaxNotGreaterThanMinYieldsUniformMinField()
    {
        var lum = new[] { 0.0, 0.5, 1.0, 0.25 };
        var p = Defaults();
        p.MinThicknessMm = 2.0;
        p.MaxThicknessMm = 1.0;

        var field = LithophaneEngine.Compute(lum, 2, 2, p);

        Assert.NotNull(field);
        Assert.All(field!.Heights, h => Assert.Equal(2.0, h, 9));
    }
}
