using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class ModelOffsetEngineTests
{
    /// <summary>8x8 grid: 4x4 plateau (z=6) on a floor of 1, centered at cells 2..5.</summary>
    private static HeightfieldData Plateau()
    {
        var h = new double[64];
        Array.Fill(h, 1.0);
        for (int j = 2; j < 6; j++)
            for (int i = 2; i < 6; i++)
                h[j * 8 + i] = 6.0;
        return new HeightfieldData(8, 8, 1.0, 0, 0, h);
    }

    [Fact]
    public void Zero_Offset_Is_Identity()
    {
        var hf = Plateau();
        var r = ModelOffsetEngine.Offset(hf, new ModelOffsetEngine.OffsetParams { OffsetMm = 0 })!;
        Assert.Equal(0, r.ChangedCellCount);
        Assert.Equal(hf.MaxHeight, r.MaxHeightAfter, 6);
    }

    [Fact]
    public void Dilation_Raises_Boundary_Ring()
    {
        var hf = Plateau();
        var r = ModelOffsetEngine.Offset(hf, new ModelOffsetEngine.OffsetParams { OffsetMm = 2 })!;
        Assert.True(r.ChangedCellCount > 0);
        // Cells just outside the plateau (ring at distance ≤ 2) got raised.
        // Cell (1,4): outside plateau, within 2 cells → raised to ~6.
        Assert.True(r.Heightfield.Heights[4 * 8 + 1] > 1.5);
        // Material tops untouched.
        Assert.Equal(6.0, r.Heightfield.Heights[3 * 8 + 3], 6);
        Assert.Equal(6.0, r.MaxHeightAfter, 6);
    }

    [Fact]
    public void Erosion_Lowers_Boundary_Ring()
    {
        var hf = Plateau();
        var r = ModelOffsetEngine.Offset(hf, new ModelOffsetEngine.OffsetParams { OffsetMm = -2 })!;
        Assert.True(r.ChangedCellCount > 0);
        // Boundary material cells lowered toward the floor.
        Assert.True(r.Heightfield.Heights[2 * 8 + 2] < 6.0);
        // Interior (cell 3,3) stays at full height.
        Assert.Equal(6.0, r.Heightfield.Heights[3 * 8 + 3], 6);
    }

    [Fact]
    public void Uniform_Grid_Is_Noop()
    {
        var flat = new HeightfieldData(8, 8, 1.0, 0, 0, Enumerable.Repeat(4.0, 64).ToArray());
        var r = ModelOffsetEngine.Offset(flat, new ModelOffsetEngine.OffsetParams { OffsetMm = 3 })!;
        Assert.Equal(0, r.ChangedCellCount);
    }
}
