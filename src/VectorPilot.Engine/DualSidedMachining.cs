using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>Which face of the stock a toolpath cuts.</summary>
public enum StockSide { Top, Bottom }

/// <summary>How the stock is turned over between sides.</summary>
public enum FlipAxis
{
    /// <summary>Flip left-to-right: X is mirrored, Y is preserved.</summary>
    Vertical,
    /// <summary>Flip top-to-bottom: Y is mirrored, X is preserved.</summary>
    Horizontal
}

/// <summary>
/// Two-sided machining. Cutting the second face requires transforming its geometry
/// into the coordinates the machine will see after the stock is physically turned
/// over — get the mirror axis wrong and the back cuts land mirrored, ruining the
/// part. The transform is an involution: applying it twice returns the original.
/// </summary>
public static class DualSidedMachining
{
    /// <summary>
    /// Map a point from design space to machine space for the given side.
    /// Top is identity; bottom mirrors about the stock centre on the flip axis.
    /// </summary>
    public static VectorPoint MapPoint(
        VectorPoint p, StockSide side, FlipAxis axis, double stockWidth, double stockHeight)
    {
        if (side == StockSide.Top) return p;

        return axis == FlipAxis.Vertical
            ? new VectorPoint(stockWidth - p.X, p.Y)
            : new VectorPoint(p.X, stockHeight - p.Y);
    }

    /// <summary>Map a whole shape, preserving winding order semantics for the caller.</summary>
    public static VectorShape MapShape(
        VectorShape shape, StockSide side, FlipAxis axis, double stockWidth, double stockHeight)
    {
        var mapped = new VectorShape
        {
            Type = shape.Type,
            Closed = shape.Closed,
            Radius = shape.Radius
        };
        foreach (var p in shape.Points)
            mapped.Points.Add(MapPoint(p, side, axis, stockWidth, stockHeight));
        return mapped;
    }

    /// <summary>
    /// Z for a bottom-side cut. Depths are measured from whichever face is up, so a
    /// bottom cut of <paramref name="depthBelowSurface"/> is the same Z value — the
    /// stock has been turned over, not the tool. Returned negative.
    /// </summary>
    public static double MapDepth(double depthBelowSurface)
        => -Math.Abs(depthBelowSurface);

    /// <summary>
    /// Remaining web thickness between cuts on both faces. Negative means the two
    /// sides overlap and the part will fall free — usually a mistake unless it is a
    /// deliberate through-cut.
    /// </summary>
    public static double WebThickness(double stockThickness, double topDepth, double bottomDepth)
        => stockThickness - Math.Abs(topDepth) - Math.Abs(bottomDepth);

    /// <summary>True when top and bottom cuts meet or overlap.</summary>
    public static bool CutsThrough(double stockThickness, double topDepth, double bottomDepth)
        => WebThickness(stockThickness, topDepth, bottomDepth) <= 0;

    /// <summary>
    /// Registration hole positions for aligning the stock after the flip. Holes are
    /// placed outside the part on the flip axis so they survive the turn-over, and
    /// mirrored positions must coincide with the originals — that is what makes them
    /// usable as datums.
    /// </summary>
    public static List<VectorPoint> RegistrationHoles(
        double stockWidth, double stockHeight, FlipAxis axis, double margin = 12.0)
    {
        var holes = new List<VectorPoint>();
        if (stockWidth <= margin * 2 || stockHeight <= margin * 2) return holes;

        if (axis == FlipAxis.Vertical)
        {
            // Mirrored about X: place pairs symmetric in X so they map onto each other.
            holes.Add(new VectorPoint(margin, margin));
            holes.Add(new VectorPoint(stockWidth - margin, margin));
            holes.Add(new VectorPoint(margin, stockHeight - margin));
            holes.Add(new VectorPoint(stockWidth - margin, stockHeight - margin));
        }
        else
        {
            holes.Add(new VectorPoint(margin, margin));
            holes.Add(new VectorPoint(margin, stockHeight - margin));
            holes.Add(new VectorPoint(stockWidth - margin, margin));
            holes.Add(new VectorPoint(stockWidth - margin, stockHeight - margin));
        }
        return holes;
    }

    /// <summary>
    /// Setup instructions for the operator, emitted between the two programs.
    /// Getting this wrong is the most common way a two-sided job is ruined.
    /// </summary>
    public static List<string> FlipInstructions(FlipAxis axis, double stockThickness)
    {
        string direction = axis == FlipAxis.Vertical
            ? "left-to-right (mirror about the Y axis, keep the front edge forward)"
            : "front-to-back (mirror about the X axis, keep the left edge on the left)";

        return new List<string>
        {
            "(--- FLIP THE STOCK ---)",
            "M5 ; spindle off",
            "M0 ; pause for operator",
            $"(1. Turn the stock over {direction})",
            "(2. Seat it against the same two datum edges)",
            "(3. Re-zero Z on the NEW top surface)",
            $"(4. Stock thickness {stockThickness:0.###} mm — confirm before resuming)",
            "(5. Verify the registration holes line up)"
        };
    }
}
