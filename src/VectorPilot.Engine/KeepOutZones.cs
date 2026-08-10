using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>Keep-out zone shape (ported from KeepOutZones.swift).</summary>
public enum KeepOutZoneType { Circle, Rectangle, Polygon }

/// <summary>A zone toolpaths must avoid (ported from KeepOutZone.swift).</summary>
public sealed class KeepOutZone
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Keep Out";
    public KeepOutZoneType Type { get; init; }
    public VectorPoint? CircleCenter { get; init; }
    public double? CircleRadiusMm { get; init; }
    public double? RectMinX { get; init; }
    public double? RectMinY { get; init; }
    public double? RectMaxX { get; init; }
    public double? RectMaxY { get; init; }
    public List<VectorPoint>? PolygonPoints { get; init; }
    public bool IsActive { get; set; } = true;

    public bool ContainsPoint(VectorPoint p)
    {
        if (!IsActive) return false;
        switch (Type)
        {
            case KeepOutZoneType.Circle:
                if (CircleCenter is not { } c || CircleRadiusMm is not { } r) return false;
                double dx = p.X - c.X, dy = p.Y - c.Y;
                return dx * dx + dy * dy <= r * r;
            case KeepOutZoneType.Rectangle:
                if (RectMinX is null || RectMinY is null || RectMaxX is null || RectMaxY is null) return false;
                return p.X >= RectMinX && p.X <= RectMaxX && p.Y >= RectMinY && p.Y <= RectMaxY;
            case KeepOutZoneType.Polygon:
                return PolygonPoints is { Count: >= 3 } pts && PointInPolygon(p, pts);
        }
        return false;
    }

    public bool IntersectsLine(VectorPoint start, VectorPoint end)
    {
        if (!IsActive) return false;
        if (ContainsPoint(start) || ContainsPoint(end)) return true;
        if (Type == KeepOutZoneType.Rectangle && RectMinX is not null && RectMinY is not null && RectMaxX is not null && RectMaxY is not null)
        {
            double lineMinX = Math.Min(start.X, end.X), lineMaxX = Math.Max(start.X, end.X);
            double lineMinY = Math.Min(start.Y, end.Y), lineMaxY = Math.Max(start.Y, end.Y);
            return !(lineMaxX < RectMinX || lineMinX > RectMaxX || lineMaxY < RectMinY || lineMinY > RectMaxY);
        }
        return false;
    }

    public static bool PointInPolygon(VectorPoint p, List<VectorPoint> polygon)
    {
        bool inside = false;
        int n = polygon.Count;
        int j = n - 1;
        for (int i = 0; i < n; i++)
        {
            double yi = polygon[i].Y, yj = polygon[j].Y;
            double xi = polygon[i].X, xj = polygon[j].X;
            if ((yi > p.Y) != (yj > p.Y) &&
                p.X < (xj - xi) * (p.Y - yi) / (yj - yi) + xi)
            {
                inside = !inside;
            }
            j = i;
        }
        return inside;
    }
}

/// <summary>Collection manager (ported from KeepOutZoneManager.swift).</summary>
public sealed class KeepOutZoneManager
{
    public List<KeepOutZone> Zones { get; } = new();

    public void AddZone(KeepOutZone zone) => Zones.Add(zone);

    public bool RemoveZone(Guid id)
    {
        int idx = Zones.FindIndex(z => z.Id == id);
        if (idx < 0) return false;
        Zones.RemoveAt(idx);
        return true;
    }

    public bool ContainsPoint(VectorPoint p) => Zones.Any(z => z.IsActive && z.ContainsPoint(p));

    public bool IntersectsLine(VectorPoint start, VectorPoint end) => Zones.Any(z => z.IsActive && z.IntersectsLine(start, end));

    public List<KeepOutZone> ActiveZones => Zones.Where(z => z.IsActive).ToList();

    public void ClearAll() => Zones.Clear();
}
