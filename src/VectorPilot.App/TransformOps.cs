using VectorPilot.Engine;
using VectorPilot.Geometry;

namespace VectorPilot.App;

/// <summary>
/// Card A3: exact numeric transforms on a canvas selection — set position/size,
/// rotate by angle, scale by factor. Mutates the selected shapes in place so
/// layer membership and identity survive (undo snapshots handle restore).
/// </summary>
public static class TransformOps
{
    /// <summary>Combined bounds of a shape set, or null when empty.</summary>
    public static (double MinX, double MinY, double MaxX, double MaxY)? Bounds(IReadOnlyList<VectorShape> shapes)
    {
        if (shapes.Count == 0) return null;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var s in shapes)
        {
            var b = SelectionModel.ShapeBounds(s);
            minX = Math.Min(minX, b.MinX); minY = Math.Min(minY, b.MinY);
            maxX = Math.Max(maxX, b.MaxX); maxY = Math.Max(maxY, b.MaxY);
        }
        return (minX, minY, maxX, maxY);
    }

    public static VectorPoint Center(IReadOnlyList<VectorShape> shapes)
    {
        var b = Bounds(shapes);
        return b is null ? new VectorPoint(0, 0)
            : new VectorPoint((b.Value.MinX + b.Value.MaxX) / 2, (b.Value.MinY + b.Value.MaxY) / 2);
    }

    /// <summary>Translate so the selection's lower-left corner lands on (x, y).</summary>
    public static bool SetPosition(IReadOnlyList<VectorShape> shapes, double x, double y)
    {
        var b = Bounds(shapes);
        if (b is null) return false;
        Translate(shapes, x - b.Value.MinX, y - b.Value.MinY);
        return true;
    }

    /// <summary>
    /// Resize the selection to an exact width/height about its lower-left corner.
    /// With <paramref name="uniform"/> the larger required factor is applied to both
    /// axes so the aspect ratio is preserved.
    /// </summary>
    public static bool SetSize(IReadOnlyList<VectorShape> shapes, double width, double height, bool uniform = false)
    {
        var b = Bounds(shapes);
        if (b is null) return false;

        double curW = b.Value.MaxX - b.Value.MinX;
        double curH = b.Value.MaxY - b.Value.MinY;
        if (curW < 1e-9 || curH < 1e-9 || width <= 0 || height <= 0) return false;

        double sx = width / curW, sy = height / curH;
        if (uniform) { double f = Math.Min(sx, sy); sx = sy = f; }

        ScaleXY(shapes, sx, sy, new VectorPoint(b.Value.MinX, b.Value.MinY));
        return true;
    }

    /// <summary>Uniform scale about the selection's bbox center.</summary>
    public static bool ScaleBy(IReadOnlyList<VectorShape> shapes, double factor)
    {
        if (shapes.Count == 0 || factor <= 0) return false;
        ScaleXY(shapes, factor, factor, Center(shapes));
        return true;
    }

    /// <summary>Rotate about the selection's bbox center (degrees, CCW).</summary>
    public static bool RotateBy(IReadOnlyList<VectorShape> shapes, double angleDegrees)
    {
        if (shapes.Count == 0) return false;
        var c = Center(shapes);
        double rad = angleDegrees * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);

        foreach (var s in shapes)
        {
            for (int i = 0; i < s.Points.Count; i++)
            {
                double dx = s.Points[i].X - c.X, dy = s.Points[i].Y - c.Y;
                s.Points[i] = new VectorPoint(c.X + dx * cos - dy * sin, c.Y + dx * sin + dy * cos);
            }
        }
        return true;
    }

    public static void Translate(IReadOnlyList<VectorShape> shapes, double dx, double dy)
    {
        foreach (var s in shapes)
            for (int i = 0; i < s.Points.Count; i++)
                s.Points[i] = new VectorPoint(s.Points[i].X + dx, s.Points[i].Y + dy);
    }

    private static void ScaleXY(IReadOnlyList<VectorShape> shapes, double sx, double sy, VectorPoint about)
    {
        foreach (var s in shapes)
        {
            for (int i = 0; i < s.Points.Count; i++)
            {
                s.Points[i] = new VectorPoint(
                    about.X + (s.Points[i].X - about.X) * sx,
                    about.Y + (s.Points[i].Y - about.Y) * sy);
            }
            // Circles carry radius separately; scale it by the mean factor.
            if (s.Type == ShapeType.Circle) s.Radius *= (Math.Abs(sx) + Math.Abs(sy)) / 2.0;
        }
    }
}
