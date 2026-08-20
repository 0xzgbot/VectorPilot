using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Applies a <see cref="NestResult"/> to real shapes: the engine computes placements but
/// nothing ever moved the geometry, so nesting had no effect on a document.
///
/// This is the single code path the Design "Nest" button uses, so a test that calls
/// <see cref="Apply"/> is exercising exactly what the user clicks.
/// </summary>
public static class NestApply
{
    public sealed class Outcome
    {
        public int Placed { get; init; }
        public int Unplaced { get; init; }
        public double Utilization { get; init; }
        public string? Error { get; init; }
        public bool Ok => Error is null;
    }

    /// <summary>
    /// Nest <paramref name="shapes"/> onto a sheet and MOVE them to their placements,
    /// mutating the shapes in place (the caller snapshots for undo first).
    /// </summary>
    public static Outcome Apply(
        IReadOnlyList<VectorShape> shapes,
        double sheetWidth, double sheetHeight,
        double spacingMm = 2.0,
        bool allowRotation = true)
    {
        if (shapes.Count == 0)
            return new Outcome { Error = "Select closed shapes to nest." };

        if (sheetWidth <= 0 || sheetHeight <= 0)
            return new Outcome { Error = "The sheet has no size — set width and height in Setup." };

        // Nesting only means anything for closed outlines: an open path has no area to
        // pack. Refuse rather than silently packing bounding boxes of open curves.
        var closed = shapes.Where(s => s.Closed || s.Type == ShapeType.Circle
                                        || s.Type == ShapeType.Rectangle).ToList();
        if (closed.Count == 0)
            return new Outcome { Error = "Nesting needs closed shapes — the selection is all open paths." };

        // margin = sheet border, spacing = gap BETWEEN parts. These are different things
        // and were previously conflated: passing spacing as margin only inset the sheet
        // edge and left parts packed flush, i.e. no room for the cutter between them.
        var result = NestingEngine.Nest(closed, sheetWidth, sheetHeight,
                                        margin: 5.0, spacing: Math.Max(0, spacingMm));
        if (result.IsEmpty)
            return new Outcome { Error = "Nothing fitted on the sheet — try a larger sheet or smaller spacing." };

        int placed = 0;
        foreach (var part in result.Parts)
        {
            // Map the engine's placement back onto the caller's shape instance.
            var shape = part.Shape;
            var bb = BoundsOf(shape);

            if (allowRotation && Math.Abs(part.Rotation) > 0.5)
            {
                var centre = new VectorPoint((bb.MinX + bb.MaxX) / 2, (bb.MinY + bb.MaxY) / 2);
                RotateInPlace(shape, part.Rotation, centre);
                bb = BoundsOf(shape);
            }

            // Position is the placement's lower-left corner.
            MoveInPlace(shape, part.Position.X - bb.MinX, part.Position.Y - bb.MinY);
            placed++;
        }

        return new Outcome
        {
            Placed = placed,
            Unplaced = result.UnplacedCount,
            Utilization = result.Utilization
        };
    }

    private static (double MinX, double MinY, double MaxX, double MaxY) BoundsOf(VectorShape s)
    {
        if (s.Points.Count == 0) return (0, 0, 0, 0);
        return (s.Points.Min(p => p.X), s.Points.Min(p => p.Y),
                s.Points.Max(p => p.X), s.Points.Max(p => p.Y));
    }

    private static void MoveInPlace(VectorShape s, double dx, double dy)
    {
        for (int i = 0; i < s.Points.Count; i++)
            s.Points[i] = new VectorPoint(s.Points[i].X + dx, s.Points[i].Y + dy);
    }

    private static void RotateInPlace(VectorShape s, double degrees, VectorPoint centre)
    {
        for (int i = 0; i < s.Points.Count; i++)
            s.Points[i] = Transform2D.Rotate(s.Points[i], centre, degrees);
    }
}
