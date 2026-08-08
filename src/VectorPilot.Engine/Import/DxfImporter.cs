using System.Globalization;
using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Minimal ASCII DXF importer: reads the ENTITIES section and converts
/// LINE / LWPOLYLINE / POLYLINE+VERTEX / CIRCLE / ARC entities into
/// <see cref="VectorShape"/> values. Ported from ShopPilotGeometry.DXFParser (Swift).
/// </summary>
/// <remarks>
/// Tolerant by design: unsupported entities (TEXT, INSERT, SPLINE, ...) are skipped
/// and malformed group pairs are ignored — parsing never throws.
/// </remarks>
public static class DxfImporter
{
    private const NumberStyles NumStyle = NumberStyles.Float;

    /// <summary>Parse ASCII DXF text (R12-style) into shapes. Never throws.</summary>
    public static List<VectorShape> Parse(string content)
    {
        var shapes = new List<VectorShape>();
        if (content is null) return shapes;

        var lines = content
            .Split(new[] { '\n', '\r' }, StringSplitOptions.None)
            .Select(l => l.Trim())
            .ToArray();
        if (lines.Length == 0) return shapes;

        var section = "";
        var i = 0;
        while (i + 1 < lines.Length)
        {
            var code = lines[i];
            var value = lines[i + 1];
            i += 2;

            if (code != "0") continue;

            switch (value)
            {
                case "SECTION":
                    // The next pair names the section (2 ENTITIES / 2 HEADER / ...).
                    if (i + 1 < lines.Length && lines[i] == "2")
                    {
                        section = lines[i + 1];
                        i += 2;
                    }
                    break;
                case "ENDSEC":
                    section = "";
                    break;
                case "EOF":
                    i = lines.Length;
                    break;
                default:
                    if (section != "ENTITIES") break;
                    switch (value)
                    {
                        case "LINE": i = ParseLine(lines, i, shapes); break;
                        case "LWPOLYLINE": i = ParseLwPolyline(lines, i, shapes); break;
                        case "POLYLINE": i = ParsePolyline(lines, i, shapes); break;
                        case "CIRCLE": i = ParseCircle(lines, i, shapes); break;
                        case "ARC": i = ParseArc(lines, i, shapes); break;
                        default: i = SkipEntity(lines, i); break;
                    }
                    break;
            }
        }

        return shapes;
    }

    // MARK: - Entity parsers (consume pairs up to the next 0-group)

    private static int ParseLine(string[] lines, int i, List<VectorShape> shapes)
    {
        double? x1 = null, y1 = null, x2 = null, y2 = null;
        var j = Collect(lines, i, (code, value) =>
        {
            if (!TryNum(value, out var d)) return;
            switch (code)
            {
                case "10": x1 = d; break;
                case "20": y1 = d; break;
                case "11": x2 = d; break;
                case "21": y2 = d; break;
            }
        });
        if (x1 is double a && y1 is double b && x2 is double c && y2 is double d)
            shapes.Add(VectorShape.Line(new VectorPoint(a, b), new VectorPoint(c, d)));
        return j;
    }

    private static int ParseLwPolyline(string[] lines, int i, List<VectorShape> shapes)
    {
        var vertices = new List<VectorPoint>();
        var closed = false;
        double? pendingX = null;
        var j = Collect(lines, i, (code, value) =>
        {
            if (!TryNum(value, out var d)) return;
            switch (code)
            {
                case "10":
                    pendingX = d;
                    break;
                case "20":
                    if (pendingX is double px)
                    {
                        vertices.Add(new VectorPoint(px, d));
                        pendingX = null;
                    }
                    break;
                case "70":
                    closed = ((int)d & 1) != 0;
                    break;
            }
        });
        if (vertices.Count >= 2)
        {
            var points = new List<VectorPoint>(vertices);
            if (closed && points[^1] != points[0]) points.Add(points[0]);
            shapes.Add(VectorShape.Polyline(points, closed));
        }
        return j;
    }

    /// <summary>POLYLINE + VERTEX entities fallback (R12 style, with SEQEND terminator).</summary>
    private static int ParsePolyline(string[] lines, int i, List<VectorShape> shapes)
    {
        var closed = false;
        var j = Collect(lines, i, (code, value) =>
        {
            if (code == "70" && TryNum(value, out var d)) closed = ((int)d & 1) != 0;
        });

        var vertices = new List<VectorPoint>();
        while (j + 1 < lines.Length && lines[j] == "0")
        {
            var entity = lines[j + 1];
            if (entity == "VERTEX")
            {
                double? px = null, py = null;
                j = Collect(lines, j + 2, (code, value) =>
                {
                    if (code == "10" && TryNum(value, out var vx)) px = vx;
                    else if (code == "20" && TryNum(value, out var vy)) py = vy;
                });
                if (px is double vx && py is double vy) vertices.Add(new VectorPoint(vx, vy));
            }
            else if (entity == "SEQEND")
            {
                j = Collect(lines, j + 2, static (_, _) => { });
                break;
            }
            else
            {
                break;
            }
        }

        if (vertices.Count >= 2)
        {
            var points = new List<VectorPoint>(vertices);
            if (closed && points[^1] != points[0]) points.Add(points[0]);
            shapes.Add(VectorShape.Polyline(points, closed));
        }
        return j;
    }

    private static int ParseCircle(string[] lines, int i, List<VectorShape> shapes)
    {
        double? cx = null, cy = null, radius = null;
        var j = Collect(lines, i, (code, value) =>
        {
            if (!TryNum(value, out var d)) return;
            switch (code)
            {
                case "10": cx = d; break;
                case "20": cy = d; break;
                case "40": radius = d; break;
            }
        });
        if (cx is double x && cy is double y && radius is double r)
            shapes.Add(VectorShape.Circle(new VectorPoint(x, y), r));
        return j;
    }

    private static int ParseArc(string[] lines, int i, List<VectorShape> shapes)
    {
        double? cx = null, cy = null, radius = null, startDeg = null, endDeg = null;
        var j = Collect(lines, i, (code, value) =>
        {
            if (!TryNum(value, out var d)) return;
            switch (code)
            {
                case "10": cx = d; break;
                case "20": cy = d; break;
                case "40": radius = d; break;
                case "50": startDeg = d; break;
                case "51": endDeg = d; break;
            }
        });
        if (cx is double x && cy is double y && radius is double r
            && startDeg is double s && endDeg is double e)
        {
            var arc = new VectorShape
            {
                Type = ShapeType.Arc,
                Radius = r,
                StartAngleDeg = s,
                EndAngleDeg = e
            };
            arc.Points.Add(new VectorPoint(x, y));
            shapes.Add(arc);
        }
        return j;
    }

    /// <summary>Consume an unsupported entity's pairs (up to the next 0-group).</summary>
    private static int SkipEntity(string[] lines, int i)
    {
        var j = i;
        while (j + 1 < lines.Length)
        {
            if (lines[j] == "0") break;
            j += 2;
        }
        return j;
    }

    /// <summary>
    /// Walk pairs until the next 0-group, handing each (code, value) to the
    /// visitor. Returns the index after the terminator (points at the 0).
    /// </summary>
    private static int Collect(string[] lines, int i, Action<string, string> visit)
    {
        var j = i;
        while (j + 1 < lines.Length)
        {
            var code = lines[j];
            if (code == "0") break;
            visit(code, lines[j + 1]);
            j += 2;
        }
        return j;
    }

    private static bool TryNum(string value, out double d) =>
        double.TryParse(value, NumStyle, CultureInfo.InvariantCulture, out d);
}
