using VectorPilot.Engine;
using VectorPilot.Geometry;

namespace VectorPilot.App;

/// <summary>
/// Canvas selection + direct-manipulation model for the design panel:
/// hit-testing, marquee selection, move/nudge, delete, duplicate.
/// Pure logic (no WPF types) so it is unit-testable.
/// </summary>
public sealed class SelectionModel
{
    private readonly List<VectorShape> _selected = new();

    public IReadOnlyList<VectorShape> Selected => _selected;
    public int Count => _selected.Count;
    public bool IsEmpty => _selected.Count == 0;

    public void Clear() => _selected.Clear();

    public bool IsSelected(VectorShape s) => _selected.Contains(s);

    public void Select(VectorShape s, bool additive = false)
    {
        if (!additive) _selected.Clear();
        if (!_selected.Contains(s)) _selected.Add(s);
    }

    public void Toggle(VectorShape s)
    {
        if (!_selected.Remove(s)) _selected.Add(s);
    }

    public void SelectAll(Layer layer)
    {
        _selected.Clear();
        _selected.AddRange(layer.Shapes);
    }

    /// <summary>Hit-test a point against a layer's shapes, nearest-first within tolerance.</summary>
    public static VectorShape? HitTest(Layer layer, VectorPoint p, double tolerance)
    {
        VectorShape? best = null;
        double bestDist = double.MaxValue;
        foreach (var shape in layer.Shapes)
        {
            double d = DistanceToShape(shape, p);
            if (d <= tolerance && d < bestDist) { bestDist = d; best = shape; }
        }
        return best;
    }

    /// <summary>Select every shape whose bounding box is fully inside the marquee.</summary>
    public void SelectInRect(Layer layer, VectorPoint corner1, VectorPoint corner2, bool additive = false)
    {
        double minX = Math.Min(corner1.X, corner2.X), maxX = Math.Max(corner1.X, corner2.X);
        double minY = Math.Min(corner1.Y, corner2.Y), maxY = Math.Max(corner1.Y, corner2.Y);
        if (!additive) _selected.Clear();
        foreach (var shape in layer.Shapes)
        {
            var b = ShapeBounds(shape);
            if (b.MinX >= minX && b.MaxX <= maxX && b.MinY >= minY && b.MaxY <= maxY)
            {
                if (!_selected.Contains(shape)) _selected.Add(shape);
            }
        }
    }

    /// <summary>Translate every selected shape.</summary>
    public void MoveSelected(double dx, double dy)
    {
        foreach (var shape in _selected)
        {
            for (int i = 0; i < shape.Points.Count; i++)
            {
                shape.Points[i] = new VectorPoint(shape.Points[i].X + dx, shape.Points[i].Y + dy);
            }
        }
    }

    /// <summary>Remove selected shapes from a layer. Returns how many were removed.</summary>
    public int DeleteSelected(Layer layer)
    {
        int removed = 0;
        foreach (var shape in _selected.ToList())
        {
            if (layer.Shapes.Remove(shape)) removed++;
        }
        _selected.Clear();
        return removed;
    }

    /// <summary>Duplicate selected shapes with an offset; the copies become the new selection.</summary>
    public List<VectorShape> DuplicateSelected(Layer layer, double dx, double dy)
    {
        var copies = new List<VectorShape>();
        foreach (var shape in _selected)
        {
            var copy = UndoStack.CloneShape(shape);
            for (int i = 0; i < copy.Points.Count; i++)
            {
                copy.Points[i] = new VectorPoint(copy.Points[i].X + dx, copy.Points[i].Y + dy);
            }
            layer.Shapes.Add(copy);
            copies.Add(copy);
        }
        _selected.Clear();
        _selected.AddRange(copies);
        return copies;
    }

    /// <summary>Combined bounding box of the selection (null when empty).</summary>
    public (double MinX, double MinY, double MaxX, double MaxY)? SelectionBounds()
    {
        if (_selected.Count == 0) return null;
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var shape in _selected)
        {
            var b = ShapeBounds(shape);
            minX = Math.Min(minX, b.MinX); minY = Math.Min(minY, b.MinY);
            maxX = Math.Max(maxX, b.MaxX); maxY = Math.Max(maxY, b.MaxY);
        }
        return (minX, minY, maxX, maxY);
    }

    // ---- geometry helpers ----

    public static (double MinX, double MinY, double MaxX, double MaxY) ShapeBounds(VectorShape shape)
    {
        if (shape.Type == ShapeType.Circle && shape.Points.Count == 1)
        {
            var c = shape.Points[0];
            return (c.X - shape.Radius, c.Y - shape.Radius, c.X + shape.Radius, c.Y + shape.Radius);
        }
        if (shape.Points.Count == 0) return (0, 0, 0, 0);
        double minX = shape.Points.Min(p => p.X), maxX = shape.Points.Max(p => p.X);
        double minY = shape.Points.Min(p => p.Y), maxY = shape.Points.Max(p => p.Y);
        return (minX, minY, maxX, maxY);
    }

    private static double DistanceToShape(VectorShape shape, VectorPoint p)
    {
        if (shape.Type == ShapeType.Circle && shape.Points.Count == 1)
        {
            double d = Dist(shape.Points[0], p);
            return Math.Abs(d - shape.Radius); // distance to the ring
        }
        if (shape.Points.Count == 1) return Dist(shape.Points[0], p);
        if (shape.Points.Count == 0) return double.MaxValue;

        double best = double.MaxValue;
        int segments = shape.Closed ? shape.Points.Count : shape.Points.Count - 1;
        for (int i = 0; i < segments; i++)
        {
            var a = shape.Points[i];
            var b = shape.Points[(i + 1) % shape.Points.Count];
            best = Math.Min(best, DistanceToSegment(a, b, p));
        }
        return best;
    }

    private static double DistanceToSegment(VectorPoint a, VectorPoint b, VectorPoint p)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double lenSq = dx * dx + dy * dy;
        if (lenSq < 1e-12) return Dist(a, p);
        double t = ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / lenSq;
        t = Math.Clamp(t, 0, 1);
        return Dist(new VectorPoint(a.X + t * dx, a.Y + t * dy), p);
    }

    private static double Dist(VectorPoint a, VectorPoint b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
