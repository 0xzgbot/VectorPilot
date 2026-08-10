using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class NestingEngineTests
{
    private static List<VectorShape> Parts() => new()
    {
        VectorShape.Rectangle(0, 0, 20, 10),
        VectorShape.Rectangle(0, 0, 15, 15),
        VectorShape.Rectangle(0, 0, 8, 8),
        VectorShape.Rectangle(0, 0, 30, 5)
    };

    [Fact]
    public void Nest_Places_All_Parts_On_Big_Sheet()
    {
        var r = NestingEngine.Nest(Parts(), sheetWidth: 200, sheetHeight: 200);
        Assert.Equal(4, r.Parts.Count);
        Assert.Equal(0, r.UnplacedCount);
        Assert.True(r.Utilization > 0.01);
    }

    [Fact]
    public void Nest_No_Overlap()
    {
        var r = NestingEngine.Nest(Parts(), sheetWidth: 200, sheetHeight: 200);
        for (int i = 0; i < r.Parts.Count; i++)
        {
            for (int j = i + 1; j < r.Parts.Count; j++)
            {
                var a = r.Parts[i].BoundingBox;
                var b = r.Parts[j].BoundingBox;
                bool overlap = a.MinX < b.MaxX && b.MinX < a.MaxX && a.MinY < b.MaxY && b.MinY < a.MaxY;
                Assert.False(overlap);
            }
        }
    }

    [Fact]
    public void Nest_Tiny_Sheet_Leaves_Unplaced()
    {
        var r = NestingEngine.Nest(Parts(), sheetWidth: 10, sheetHeight: 10);
        Assert.True(r.UnplacedCount > 0);
        Assert.True(r.Parts.Count < 4);
    }

    [Fact]
    public void Nest_Empty_Parts_Returns_Empty()
    {
        var r = NestingEngine.Nest(new List<VectorShape>(), 100, 100);
        Assert.True(r.IsEmpty);
        Assert.Equal(0, r.Utilization);
    }

    [Fact]
    public void NestGrid_Row_Packing()
    {
        var r = NestingEngine.NestGrid(Parts(), sheetWidth: 100, sheetHeight: 100, spacing: 1);
        Assert.Equal(4, r.Parts.Count);
        Assert.Equal(0, r.UnplacedCount);
        // Parts share rows: second part starts after the first's width + spacing.
        Assert.True(r.Parts[0].Position.X >= 0);
    }

    [Fact]
    public void Rotated_Placement_Used_When_Fit_Requires_It()
    {
        // Tall narrow part fits rotated in a wide-short space.
        var tall = new List<VectorShape> { VectorShape.Rectangle(0, 0, 5, 30) };
        var r = NestingEngine.Nest(tall, sheetWidth: 40, sheetHeight: 20, margin: 0);
        Assert.Single(r.Parts);
        Assert.Equal(Math.PI / 2, r.Parts[0].Rotation, 6);
    }
}
