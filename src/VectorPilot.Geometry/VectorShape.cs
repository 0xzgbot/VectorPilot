namespace VectorPilot.Geometry;

public enum ShapeType
{
    Line,
    Polyline,
    Rectangle,
    Circle,
    Arc,
    Ellipse,
    Text
}

/// <summary>
/// A vector shape in the document model. Mirrors the ShopPilot VectorShape concept:
/// a closed/open path with an optional radius (circle/arc) and transformable points.
/// </summary>
public sealed class VectorShape
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public ShapeType Type { get; set; }
    public List<VectorPoint> Points { get; } = new();
    public double Radius { get; set; }
    public double StartAngleDeg { get; set; }
    public double EndAngleDeg { get; set; }
    public bool Closed { get; set; }
    public string Text { get; set; } = string.Empty;
    public double StrokeWidth { get; set; } = 1.0;

    public static VectorShape Line(VectorPoint a, VectorPoint b) => new()
    {
        Type = ShapeType.Line,
        Points = { a, b },
        Closed = false
    };

    public static VectorShape Rectangle(double x, double y, double w, double h) => new()
    {
        Type = ShapeType.Rectangle,
        Points =
        {
            new VectorPoint(x, y), new VectorPoint(x + w, y),
            new VectorPoint(x + w, y + h), new VectorPoint(x, y + h)
        },
        Closed = true
    };

    public static VectorShape Circle(VectorPoint center, double radius) => new()
    {
        Type = ShapeType.Circle,
        Points = { center },
        Radius = radius,
        Closed = true
    };

    public static VectorShape Polyline(IEnumerable<VectorPoint> pts, bool closed = false)
    {
        var shape = new VectorShape
        {
            Type = ShapeType.Polyline,
            Closed = closed
        };
        shape.Points.AddRange(pts);
        return shape;
    }

    public BoundingBox Bounds()
    {
        var b = BoundingBox.FromPoints(Points);
        if (Type == ShapeType.Circle && Points.Count == 1)
        {
            var c = Points[0];
            b = new BoundingBox(c.X - Radius, c.Y - Radius, c.X + Radius, c.Y + Radius);
        }
        return b;
    }
}
