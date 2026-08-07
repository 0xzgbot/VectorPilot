namespace VectorPilot.Geometry;

/// <summary>2D affine transform helpers (translation, rotation, scale).</summary>
public static class Transform2D
{
    public static VectorPoint Translate(VectorPoint p, double dx, double dy) => new(p.X + dx, p.Y + dy);

    public static VectorPoint Rotate(VectorPoint p, VectorPoint center, double degrees)
    {
        double rad = degrees * Math.PI / 180.0;
        double cos = Math.Cos(rad), sin = Math.Sin(rad);
        double dx = p.X - center.X, dy = p.Y - center.Y;
        return new VectorPoint(center.X + dx * cos - dy * sin, center.Y + dx * sin + dy * cos);
    }

    public static VectorPoint Scale(VectorPoint p, VectorPoint center, double sx, double sy)
        => new(center.X + (p.X - center.X) * sx, center.Y + (p.Y - center.Y) * sy);

    public static VectorShape TransformShape(VectorShape shape, Func<VectorPoint, VectorPoint> fn)
    {
        var copy = new VectorShape
        {
            Id = shape.Id,
            Type = shape.Type,
            Radius = shape.Radius,
            StartAngleDeg = shape.StartAngleDeg,
            EndAngleDeg = shape.EndAngleDeg,
            Closed = shape.Closed,
            Text = shape.Text,
            StrokeWidth = shape.StrokeWidth
        };
        foreach (var p in shape.Points) copy.Points.Add(fn(p));
        return copy;
    }
}
