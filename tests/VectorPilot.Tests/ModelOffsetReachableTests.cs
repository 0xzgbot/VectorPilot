using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Model offset — the engine ModelPanel.DoModelOffset calls.
///
/// ModelOffsetEngine had NO VectorPilot.App call-site: inflating or deflating a relief
/// existed but no user could thicken a model before roughing it.
/// </summary>
public class ModelOffsetReachableTests
{
    /// <summary>A 60x40 dome, peak 8mm.</summary>
    private static HeightfieldData Dome(int n = 60, int m = 40)
    {
        var heights = new double[n * m];
        for (int y = 0; y < m; y++)
            for (int x = 0; x < n; x++)
            {
                double dx = (x - n / 2.0) / (n / 2.0);
                double dy = (y - m / 2.0) / (m / 2.0);
                double r = Math.Sqrt(dx * dx + dy * dy);
                heights[y * n + x] = r >= 1 ? 0 : 8.0 * Math.Cos(r * Math.PI / 2);
            }
        return new HeightfieldData(n, m, cellSizeMm: 1.0, minX: 0, minY: 0, heights: heights);
    }

    private static ModelOffsetEngine.OffsetParams P(double mm)
        => new() { OffsetMm = mm };

    // ---- the item's criteria ----

    [Fact]
    public void A_Positive_Offset_Grows_The_Material_Footprint()
    {
        // This engine offsets LATERALLY (XY dilation/erosion of the material mask), not by
        // adding mm to Z. A +3mm offset therefore fattens the dome's footprint; the peak
        // stays at 8mm. My first version of this test asserted the peak rose, which was my
        // misreading of the engine rather than a defect in it.
        var hf = Dome();
        double floor = hf.Heights.Min();
        int materialBefore = hf.Heights.Count(x => x > floor + 1e-9);

        var r = ModelOffsetEngine.Offset(hf, P(3.0));

        Assert.NotNull(r);
        int materialAfter = r!.Heightfield.Heights.Count(x => x > floor + 1e-9);

        Assert.True(materialAfter > materialBefore,
            $"material cells {materialBefore} -> {materialAfter} for a +3mm dilation");
    }

    [Fact]
    public void The_Reported_Max_Matches_The_Returned_Grid()
    {
        var r = ModelOffsetEngine.Offset(Dome(), P(2.5))!;
        Assert.Equal(r.Heightfield.Heights.Max(), r.MaxHeightAfter, 3);
    }

    [Fact]
    public void A_Zero_Offset_Is_A_No_Op()
    {
        var hf = Dome();
        var r = ModelOffsetEngine.Offset(hf, P(0.0));

        // Either refused outright, or returned an identical grid — never a changed one.
        if (r is not null)
        {
            Assert.Equal(hf.Heights.Max(), r.MaxHeightAfter, 6);
            Assert.Equal(hf.Heights, r.Heightfield.Heights);
        }
    }

    [Fact]
    public void A_Bigger_Offset_Grows_It_Further()
    {
        int Material(double mm)
        {
            var hf = Dome();
            double floor = hf.Heights.Min();
            var r = ModelOffsetEngine.Offset(hf, P(mm))!;
            return r.Heightfield.Heights.Count(x => x > floor + 1e-9);
        }

        Assert.True(Material(6.0) > Material(1.0));
    }

    // ---- a negative offset lowers it ----

    [Fact]
    public void A_Negative_Offset_Shrinks_The_Material_Footprint()
    {
        var hf = Dome();
        double floor = hf.Heights.Min();
        int materialBefore = hf.Heights.Count(x => x > floor + 1e-9);

        var r = ModelOffsetEngine.Offset(hf, P(-3.0));

        Assert.NotNull(r);
        int materialAfter = r!.Heightfield.Heights.Count(x => x > floor + 1e-9);

        Assert.True(materialAfter < materialBefore,
            $"material cells {materialBefore} -> {materialAfter} for a -3mm erosion");
    }

    [Fact]
    public void Heights_Never_Go_Negative()
    {
        // Cutting below the stock bottom is not a height — it must clamp at zero.
        var r = ModelOffsetEngine.Offset(Dome(), P(-50.0));

        if (r is not null)
            Assert.All(r.Heightfield.Heights, h => Assert.True(h >= -1e-9, $"height {h:F3} < 0"));
    }

    // ---- the grid survives intact ----

    [Fact]
    public void The_Grid_Dimensions_Are_Preserved()
    {
        var hf = Dome();
        var r = ModelOffsetEngine.Offset(hf, P(2.0))!;

        Assert.Equal(hf.Width, r.Heightfield.Width);
        Assert.Equal(hf.Height, r.Heightfield.Height);
        Assert.Equal(hf.Heights.Length, r.Heightfield.Heights.Length);
    }

    [Fact]
    public void The_Source_Heightfield_Is_Not_Mutated()
    {
        // The panel keeps the original for one-step undo, so it must stay untouched.
        var hf = Dome();
        var snapshot = (double[])hf.Heights.Clone();

        ModelOffsetEngine.Offset(hf, P(4.0));

        Assert.Equal(snapshot, hf.Heights);
    }

    [Fact]
    public void Cells_Are_Reported_As_Changed()
    {
        var r = ModelOffsetEngine.Offset(Dome(), P(2.0))!;
        Assert.True(r.ChangedCellCount > 0, "offset reported zero changed cells");
    }

    [Fact]
    public void No_NaN_Is_Produced()
    {
        var r = ModelOffsetEngine.Offset(Dome(), P(3.0))!;
        Assert.All(r.Heightfield.Heights, h => Assert.False(double.IsNaN(h), "offset produced NaN"));
    }

    // ---- undo restores the original relief (the panel's contract) ----

    [Fact]
    public void The_Pre_Offset_Grid_Can_Be_Restored_Exactly()
    {
        // What ModelPanel does: keep the original reference, swap AppState, swap back.
        var original = Dome();
        var snapshot = (double[])original.Heights.Clone();

        var offset = ModelOffsetEngine.Offset(original, P(5.0))!.Heightfield;

        // Compare the GRIDS, not the peaks: lateral dilation leaves the peak at 8mm, so a
        // max-height comparison proves nothing here.
        Assert.NotEqual(snapshot, offset.Heights);

        // The original instance is untouched, so restoring it is exact.
        Assert.Equal(snapshot, original.Heights);
    }
}
