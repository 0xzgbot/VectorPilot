using System.Text.Json;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>SPK-0907 parity: the keyhole gadget geometry.</summary>
public class KeyholeGadgetTests
{
    [Fact]
    public void Shape_Is_Closed_Loop_With_Slot_Bottom_At_Zero()
    {
        var shape = KeyholeGadget.KeyholeShape(screwHeadDiameterMm: 12, shaftDiameterMm: 4, clearanceMm: 0.5)!;
        Assert.True(shape.Closed);
        Assert.True(shape.Points.Count >= 27); // 2 slot pts + 24 arc + 2 + close
        // Closed loop: first == last.
        Assert.Equal(shape.Points[0], shape.Points[^1]);
        // Slot bottom at y = 0.
        Assert.Equal(0, shape.Points[0].Y);
        Assert.Equal(0, shape.Points[^2].Y);
    }

    [Fact]
    public void Slot_Half_Width_Is_Shaft_Over_Two_Plus_Clearance()
    {
        var points = KeyholeGadget.KeyholePath(screwHeadDiameterMm: 12, shaftDiameterMm: 4, clearanceMm: 0.5)!;
        double expectedHalfW = 4 / 2.0 + 0.5;
        // Bottom-left corner x = -halfW (centerX 0).
        Assert.Equal(-expectedHalfW, points[0].X, 6);
        Assert.Equal(expectedHalfW, points[^2].X, 6);
    }

    [Fact]
    public void Circle_Radius_Is_Head_Over_Two_Plus_Clearance()
    {
        var points = KeyholeGadget.KeyholePath(screwHeadDiameterMm: 12, shaftDiameterMm: 4, clearanceMm: 0.5)!;
        double headR = 12 / 2.0 + 0.5;
        // Arc samples lie on the circle (center (0, headR)).
        foreach (var p in points.Skip(2).Take(24))
        {
            double dx = p.X, dy = p.Y - headR;
            Assert.Equal(headR * headR, dx * dx + dy * dy, 3);
        }
    }

    [Fact]
    public void Shaft_Wider_Than_Head_Is_Degenerate()
    {
        Assert.Null(KeyholeGadget.KeyholePath(screwHeadDiameterMm: 4, shaftDiameterMm: 12));
        Assert.Null(KeyholeGadget.KeyholeShape(screwHeadDiameterMm: 6, shaftDiameterMm: 6)); // equal → degenerate too
    }

    [Fact]
    public void Shape_Survives_Json_Round_Trip()
    {
        var shape = KeyholeGadget.KeyholeShape(screwHeadDiameterMm: 10, shaftDiameterMm: 3)!;
        var json = JsonSerializer.Serialize(shape.Points);
        var back = JsonSerializer.Deserialize<List<VectorPoint>>(json)!;
        Assert.Equal(shape.Points.Count, back.Count);
        Assert.Equal(shape.Points[0], back[0]);
    }
}
