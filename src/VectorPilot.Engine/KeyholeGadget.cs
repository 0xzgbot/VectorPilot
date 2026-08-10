using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Keyhole gadget (ported from KeyholeGadget.swift, H02 / SPK-0907): a circle
/// for the screw head plus a tangent slot for the shaft, as a closed freehand
/// polyline ready for a profile cut. Circle bottom sits at y = 0.
/// </summary>
public static class KeyholeGadget
{
    public static List<VectorPoint>? KeyholePath(
        double centerX = 0,
        double screwHeadDiameterMm = 12,
        double shaftDiameterMm = 4,
        double clearanceMm = 0.5)
    {
        double headR = Math.Max(0.5, screwHeadDiameterMm / 2 + clearanceMm);
        double halfW = Math.Max(0.25, shaftDiameterMm / 2 + clearanceMm);
        if (halfW >= headR) return null; // shaft can't exceed head (degenerate)
        double centerY = headR;          // circle bottom at y = 0

        var points = new List<VectorPoint>();
        points.Add(new VectorPoint(centerX - halfW, 0));
        points.Add(new VectorPoint(centerX - halfW, centerY));
        const int arcSteps = 24;
        for (int k = 1; k <= arcSteps; k++)
        {
            double angle = Math.PI - Math.PI * k / arcSteps; // π → 0 (over the top)
            points.Add(new VectorPoint(centerX + headR * Math.Cos(angle), centerY + headR * Math.Sin(angle)));
        }
        points.Add(new VectorPoint(centerX + halfW, centerY));
        points.Add(new VectorPoint(centerX + halfW, 0));
        points.Add(new VectorPoint(centerX - halfW, 0)); // close along the bottom
        return points;
    }

    public static VectorShape? KeyholeShape(
        double centerX = 0,
        double screwHeadDiameterMm = 12,
        double shaftDiameterMm = 4,
        double clearanceMm = 0.5)
    {
        var points = KeyholePath(centerX, screwHeadDiameterMm, shaftDiameterMm, clearanceMm);
        if (points is null) return null;
        var shape = new VectorShape { Type = ShapeType.Polyline, Closed = true };
        shape.Points.AddRange(points);
        return shape;
    }
}
