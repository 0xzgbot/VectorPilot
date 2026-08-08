using System.Globalization;
using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// ASCII DXF R12 exporter (ported from VectorDXFExporter.swift, D24 lean slice).
/// LINE/CIRCLE/ARC map to native entities; other shapes sample to LWPOLYLINE.
/// </summary>
public static class DxfExporter
{
    public static string DxfString(IEnumerable<VectorShape> shapes)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("0\nSECTION\n  2\nENTITIES\n");
        foreach (var shape in shapes)
        {
            sb.Append(EntityText(shape));
        }
        sb.Append("  0\nENDSEC\n  0\nEOF\n");
        return sb.ToString();
    }

    private static string EntityText(VectorShape shape)
    {
        switch (shape.Type)
        {
            case ShapeType.Line when shape.Points.Count >= 2:
                return $"0\nLINE\n  8\n0\n 10\n{Fmt(shape.Points[0].X)}\n 20\n{Fmt(shape.Points[0].Y)}\n 30\n0.0\n 11\n{Fmt(shape.Points[1].X)}\n 21\n{Fmt(shape.Points[1].Y)}\n 31\n0.0\n";
            case ShapeType.Circle when shape.Points.Count >= 1:
                return $"0\nCIRCLE\n  8\n0\n 10\n{Fmt(shape.Points[0].X)}\n 20\n{Fmt(shape.Points[0].Y)}\n 30\n0.0\n 40\n{Fmt(shape.Radius)}\n";
            case ShapeType.Arc when shape.Points.Count >= 1:
                return $"0\nARC\n  8\n0\n 10\n{Fmt(shape.Points[0].X)}\n 20\n{Fmt(shape.Points[0].Y)}\n 30\n0.0\n 40\n{Fmt(shape.Radius)}\n 50\n{Fmt(shape.StartAngleDeg)}\n 51\n{Fmt(shape.EndAngleDeg)}\n";
            case ShapeType.Rectangle when shape.Points.Count >= 2:
            {
                var b = shape.Bounds();
                return PolylineText(new[]
                {
                    new VectorPoint(b.MinX, b.MinY), new VectorPoint(b.MaxX, b.MinY),
                    new VectorPoint(b.MaxX, b.MaxY), new VectorPoint(b.MinX, b.MaxY)
                }, closed: true);
            }
            default:
            {
                bool closed = shape.Closed && shape.Points.Count >= 3;
                var pts = closed && shape.Points.Count > 1 && shape.Points[0] == shape.Points[^1]
                    ? shape.Points.Take(shape.Points.Count - 1).ToList()
                    : shape.Points;
                return PolylineText(pts, closed);
            }
        }
    }

    private static string PolylineText(IEnumerable<VectorPoint> pts, bool closed)
    {
        var list = pts.ToList();
        var sb = new System.Text.StringBuilder();
        sb.Append($"0\nLWPOLYLINE\n  8\n0\n 90\n{list.Count}\n 70\n{(closed ? "1" : "0")}\n");
        foreach (var p in list)
        {
            sb.Append($" 10\n{Fmt(p.X)}\n 20\n{Fmt(p.Y)}\n");
        }
        return sb.ToString();
    }

    private static string Fmt(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);
}
