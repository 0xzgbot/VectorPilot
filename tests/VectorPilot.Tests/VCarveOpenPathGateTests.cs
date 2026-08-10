using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>SPK-0604 parity: the V-Carve open-vector preflight gate.</summary>
public class VCarveOpenPathGateTests
{
    [Fact]
    public void Open_Line_Blocks()
    {
        var gate = VCarveOpenPathGate.Check(new[] { VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(10, 0)) });
        Assert.NotNull(gate);
        Assert.Contains(gate!, g => g.SuggestedFix == "Close open vector");
    }

    [Fact]
    public void Open_Polyline_Blocks()
    {
        var open = VectorShape.Polyline(new[] { new VectorPoint(0, 0), new VectorPoint(5, 5), new VectorPoint(10, 0) }, closed: false);
        Assert.NotNull(VCarveOpenPathGate.Check(new[] { open }));
    }

    [Fact]
    public void Closed_Shapes_Allow()
    {
        var shapes = new[]
        {
            VectorShape.Rectangle(0, 0, 10, 10),
            VectorShape.Circle(new VectorPoint(5, 5), 2),
            VectorShape.Polyline(new[] { new VectorPoint(0, 0), new VectorPoint(4, 0), new VectorPoint(4, 4), new VectorPoint(0, 4) }, closed: true)
        };
        Assert.Null(VCarveOpenPathGate.Check(shapes));
    }

    [Fact]
    public void Mixed_Blocks_With_Exact_Open_Indices()
    {
        // [0] open line, [1] closed rect, [2] open polyline → blocked, CTA targets 0 and 2.
        var shapes = new[]
        {
            VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(5, 0)),
            VectorShape.Rectangle(0, 0, 10, 10),
            VectorShape.Polyline(new[] { new VectorPoint(0, 0), new VectorPoint(5, 5) }, closed: false)
        };
        var gate = VCarveOpenPathGate.Check(shapes);
        Assert.NotNull(gate);
        Assert.Equal(new[] { 0, 2 }, gate!.Select(g => g.ShapeIndex).OrderBy(i => i).ToArray());
        Assert.All(gate, g => Assert.False(string.IsNullOrEmpty(g.SuggestedFix)));
    }

    [Fact]
    public void Empty_Input_Allows()
    {
        Assert.Null(VCarveOpenPathGate.Check(new List<VectorShape>()));
    }
}
