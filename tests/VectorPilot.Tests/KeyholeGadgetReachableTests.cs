using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Keyhole gadget — the engine DesignPanel.DoKeyhole calls.
///
/// KeyholeGadget had NO VectorPilot.App call-site. The only route in was the blank Lua
/// buffer, which is not a one-click keyhole: the user had to write the script themselves.
/// </summary>
public class KeyholeGadgetReachableTests
{
    // ---- it produces closed geometry ----

    [Fact]
    public void The_Keyhole_Is_Closed_Geometry()
    {
        var shape = KeyholeGadget.KeyholeShape();

        Assert.NotNull(shape);
        Assert.True(shape!.Closed, "a keyhole slot must be a closed outline to pocket or profile");
        Assert.True(shape.Points.Count >= 8, $"only {shape.Points.Count} points — not a keyhole");
    }

    [Fact]
    public void The_Path_Returns_To_Its_Start()
    {
        var pts = KeyholeGadget.KeyholePath()!;

        var first = pts[0];
        var last = pts[^1];
        Assert.Equal(first.X, last.X, 6);
        Assert.Equal(first.Y, last.Y, 6);
    }

    [Fact]
    public void No_NaN_Is_Produced()
    {
        Assert.All(KeyholeGadget.KeyholeShape()!.Points, p =>
            Assert.False(double.IsNaN(p.X) || double.IsNaN(p.Y), "keyhole produced NaN"));
    }

    // ---- dimensions come from the params ----

    [Fact]
    public void The_Head_Diameter_Drives_The_Width()
    {
        double Width(double head)
        {
            var s = KeyholeGadget.KeyholeShape(screwHeadDiameterMm: head)!;
            return s.Points.Max(p => p.X) - s.Points.Min(p => p.X);
        }

        Assert.True(Width(20) > Width(10), "a bigger screw head did not widen the slot");
    }

    [Fact]
    public void The_Head_Width_Matches_The_Requested_Diameter_Plus_Clearance()
    {
        const double head = 12, clearance = 0.5;
        var s = KeyholeGadget.KeyholeShape(screwHeadDiameterMm: head, clearanceMm: clearance)!;

        double width = s.Points.Max(p => p.X) - s.Points.Min(p => p.X);
        double expected = head + clearance * 2;   // radius = head/2 + clearance

        // The arc is sampled in 24 steps, so the widest sample sits just inside the true
        // diameter (12.944 vs 13.0). Allow one chord's worth rather than demanding an exact
        // hit that polygonal sampling can never produce.
        Assert.InRange(width, expected * 0.98, expected + 1e-6);
    }

    [Fact]
    public void The_Shaft_Diameter_Drives_The_Slot_Neck()
    {
        double Neck(double shaft)
        {
            var s = KeyholeGadget.KeyholeShape(shaftDiameterMm: shaft)!;
            // The neck is the width at y=0, where the slot meets the edge.
            var atBottom = s.Points.Where(p => Math.Abs(p.Y) < 1e-6).ToList();
            return atBottom.Max(p => p.X) - atBottom.Min(p => p.X);
        }

        Assert.True(Neck(6) > Neck(3), "a thicker screw shaft did not widen the neck");
    }

    [Fact]
    public void Clearance_Widens_Both_Head_And_Neck()
    {
        var tight = KeyholeGadget.KeyholeShape(clearanceMm: 0.1)!;
        var loose = KeyholeGadget.KeyholeShape(clearanceMm: 2.0)!;

        double W(VectorShape s) => s.Points.Max(p => p.X) - s.Points.Min(p => p.X);

        Assert.True(W(loose) > W(tight));
    }

    [Fact]
    public void The_Circle_Sits_Above_The_Slot_Opening()
    {
        // The screw head pocket must be at the top, with the entry slot reaching y=0.
        var s = KeyholeGadget.KeyholeShape()!;

        Assert.Equal(0.0, s.Points.Min(p => p.Y), 6);
        Assert.True(s.Points.Max(p => p.Y) > 6, "the head pocket is not above the opening");
    }

    [Fact]
    public void Centre_X_Positions_The_Slot()
    {
        var atZero = KeyholeGadget.KeyholeShape(centerX: 0)!;
        var at100 = KeyholeGadget.KeyholeShape(centerX: 100)!;

        double MidX(VectorShape s) => (s.Points.Min(p => p.X) + s.Points.Max(p => p.X)) / 2;

        // 0.05mm tolerance: the 24-step arc sampling leaves the midpoint ~0.028mm off the
        // true centre, which is far below any cutter's resolution.
        Assert.Equal(0.0, MidX(atZero), 1);
        Assert.Equal(100.0, MidX(at100), 1);
    }

    // ---- degenerate params are refused, not fudged ----

    [Fact]
    public void A_Shaft_Wider_Than_The_Head_Is_Refused()
    {
        // Physically impossible: the screw would fall out of its own pocket.
        Assert.Null(KeyholeGadget.KeyholeShape(screwHeadDiameterMm: 4, shaftDiameterMm: 12));
        Assert.Null(KeyholeGadget.KeyholePath(screwHeadDiameterMm: 4, shaftDiameterMm: 12));
    }

    // ---- undo removes it (the panel's contract) ----

    [Fact]
    public void Undo_Removes_The_Keyhole()
    {
        var job = new Job { Name = "keyhole" };
        var layer = job.ActiveSheet.ActiveLayer;
        layer.AddShape(VectorShape.Rectangle(0, 0, 200, 100));

        int before = layer.Shapes.Count;

        var undo = new UndoStack();
        var snapshot = UndoStack.Snapshot(layer);

        // What DoKeyhole does after snapshotting.
        layer.AddShape(KeyholeGadget.KeyholeShape()!);
        undo.Push("Keyhole", layer, snapshot);

        Assert.Equal(before + 1, layer.Shapes.Count);

        Assert.Equal("Keyhole", undo.Undo());
        Assert.Equal(before, layer.Shapes.Count);
    }

    [Fact]
    public void The_Slot_Can_Be_Moved_To_A_Selection_Centre()
    {
        // The panel drops the keyhole at the selection centre via ShapeTransformer.Move,
        // which TRANSLATES by (dx,dy) — it does not centre. So the slot's midpoint lands at
        // its original midpoint plus the offset.
        var shape = KeyholeGadget.KeyholeShape()!;
        double midBefore = (shape.Points.Min(p => p.X) + shape.Points.Max(p => p.X)) / 2;

        var moved = ShapeTransformer.Move(new[] { shape }, 150, 75);

        Assert.NotEmpty(moved);
        double midAfter = (moved[0].Points.Min(p => p.X) + moved[0].Points.Max(p => p.X)) / 2;

        Assert.Equal(midBefore + 150.0, midAfter, 3);
    }

    [Fact]
    public void Centring_On_A_Selection_Requires_Subtracting_The_Slots_Own_Midpoint()
    {
        // The bug this pins: passing the target centre straight to Move lands the slot at
        // centre + its own midpoint, which is off by half the slot width every time.
        var shape = KeyholeGadget.KeyholeShape()!;
        double ownX = (shape.Points.Min(p => p.X) + shape.Points.Max(p => p.X)) / 2;
        double ownY = (shape.Points.Min(p => p.Y) + shape.Points.Max(p => p.Y)) / 2;

        const double targetX = 150, targetY = 75;
        var centred = ShapeTransformer.Move(new[] { shape }, targetX - ownX, targetY - ownY);

        double midX = (centred[0].Points.Min(p => p.X) + centred[0].Points.Max(p => p.X)) / 2;
        double midY = (centred[0].Points.Min(p => p.Y) + centred[0].Points.Max(p => p.Y)) / 2;

        Assert.Equal(targetX, midX, 3);
        Assert.Equal(targetY, midY, 3);
    }
}
