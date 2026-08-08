using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Parses Encapsulated PostScript (EPS) text into <see cref="VectorShape"/> values.
/// Ported from ShopPilotGeometry.EPSImporter (Swift).
/// </summary>
/// <remarks>
/// Honest minimal subset — not a PostScript interpreter. Supported:
/// the <c>%%BoundingBox</c> header (coordinates are offset by llx/lly) and the
/// path operators <c>moveto</c> / <c>lineto</c> / <c>curveto</c> (sampled into
/// 16 line segments) / <c>closepath</c>, with <c>newpath</c> as a subpath
/// boundary. Everything else (colors, line width, gsave/grestore, stroke/fill,
/// text, transforms, arcs, raster) is skipped gracefully. PostScript is postfix:
/// operands are pushed before the operator, so stale operands from skipped
/// operators never corrupt the path state.
/// </remarks>
public static class EpsImporter
{
    private const NumberStyles NumStyle = NumberStyles.Float;

    private static readonly Regex BoundingBoxRegex = new(
        @"%%BoundingBox:\s*([-+]?(?:\d+\.?\d*|\.\d+))\s+([-+]?(?:\d+\.?\d*|\.\d+))\s+([-+]?(?:\d+\.?\d*|\.\d+))\s+([-+]?(?:\d+\.?\d*|\.\d+))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Parse EPS text into shapes. Never throws; returns empty for non-EPS or garbage input.</summary>
    public static List<VectorShape> Parse(string content)
    {
        var shapes = new List<VectorShape>();
        if (content is null || content.Length == 0) return shapes;

        // Require the EPS/PS magic header so arbitrary text is rejected as garbage.
        if (!content.Contains("%!PS", StringComparison.Ordinal)) return shapes;

        var bbox = ExtractBoundingBox(content);
        var offsetX = bbox?.MinX ?? 0.0;
        var offsetY = bbox?.MinY ?? 0.0;

        VectorPoint Map(double x, double y) => new(x - offsetX, y - offsetY);

        var currentPath = new List<VectorPoint>();
        var isClosed = false;
        var numbers = new List<double>();

        // Commit the current subpath as a polyline. A closepath-marked subpath is
        // closed by appending the start point when it is not already there, so
        // first == last holds and the shape reads as a closed loop.
        void Flush()
        {
            if (currentPath.Count >= 2)
            {
                if (isClosed)
                {
                    var first = currentPath[0];
                    var last = currentPath[^1];
                    if (Math.Abs(first.X - last.X) > 1e-9 || Math.Abs(first.Y - last.Y) > 1e-9)
                        currentPath.Add(first);
                }
                shapes.Add(VectorShape.Polyline(currentPath, isClosed));
            }
            currentPath = new List<VectorPoint>();
            isClosed = false;
        }

        foreach (var (isNumber, value, word) in Tokenize(content))
        {
            if (isNumber)
            {
                numbers.Add(value);
                continue;
            }

            switch (word.ToLowerInvariant())
            {
                case "moveto":
                    if (numbers.Count < 2) { numbers.Clear(); break; }
                    var my = numbers[^1]; numbers.RemoveAt(numbers.Count - 1);
                    var mx = numbers[^1]; numbers.RemoveAt(numbers.Count - 1);
                    Flush();
                    currentPath = new List<VectorPoint> { Map(mx, my) };
                    isClosed = false;
                    break;

                case "lineto":
                    if (numbers.Count < 2) { numbers.Clear(); break; }
                    var ly = numbers[^1]; numbers.RemoveAt(numbers.Count - 1);
                    var lx = numbers[^1]; numbers.RemoveAt(numbers.Count - 1);
                    currentPath.Add(Map(lx, ly));
                    break;

                case "curveto":
                    if (numbers.Count < 6) { numbers.Clear(); break; }
                    var y3 = numbers[^1]; numbers.RemoveAt(numbers.Count - 1);
                    var x3 = numbers[^1]; numbers.RemoveAt(numbers.Count - 1);
                    var y2 = numbers[^1]; numbers.RemoveAt(numbers.Count - 1);
                    var x2 = numbers[^1]; numbers.RemoveAt(numbers.Count - 1);
                    var y1 = numbers[^1]; numbers.RemoveAt(numbers.Count - 1);
                    var x1 = numbers[^1]; numbers.RemoveAt(numbers.Count - 1);
                    var start = currentPath.Count > 0 ? currentPath[^1] : VectorPoint.Zero;
                    var curve = SampleCubic(start, Map(x1, y1), Map(x2, y2), Map(x3, y3), 16);
                    currentPath.AddRange(curve.Skip(1));
                    break;

                case "closepath":
                    isClosed = true;
                    break;

                case "newpath":
                    Flush();
                    break;

                default:
                    // Any other PostScript operator: its operands (if any) are stale
                    // here — drop them so they cannot feed a later path operator.
                    numbers.Clear();
                    break;
            }
        }
        Flush();

        return shapes;
    }

    // MARK: - Bounding box

    private static (double MinX, double MinY, double MaxX, double MaxY)? ExtractBoundingBox(string content)
    {
        var match = BoundingBoxRegex.Match(content);
        if (!match.Success) return null;
        if (!TryNum(match.Groups[1].Value, out var minX)
            || !TryNum(match.Groups[2].Value, out var minY)
            || !TryNum(match.Groups[3].Value, out var maxX)
            || !TryNum(match.Groups[4].Value, out var maxY))
        {
            return null;
        }
        return (minX, minY, maxX, maxY);
    }

    // MARK: - Tokenizer

    /// <summary>
    /// Tokenize EPS text: splits on whitespace, skips <c>%</c> comments and
    /// parenthesized string literals, and classifies each chunk as a number or a word.
    /// </summary>
    private static List<(bool IsNumber, double Value, string Word)> Tokenize(string content)
    {
        var tokens = new List<(bool, double, string)>();
        var current = new StringBuilder();
        var inComment = false;
        var inString = false;
        var escaped = false;

        void FlushChunk()
        {
            if (current.Length == 0) return;
            var chunk = current.ToString();
            current.Clear();
            if (TryNum(chunk, out var value))
                tokens.Add((true, value, string.Empty));
            else
                tokens.Add((false, 0, chunk));
        }

        foreach (var ch in content)
        {
            if (inComment)
            {
                if (ch == '\n' || ch == '\r') inComment = false;
                continue;
            }
            if (inString)
            {
                if (escaped) escaped = false;
                else if (ch == '\\') escaped = true;
                else if (ch == ')') inString = false;
                continue;
            }
            if (ch == '%')
            {
                FlushChunk();
                inComment = true;
                continue;
            }
            if (ch == '(')
            {
                FlushChunk();
                inString = true;
                continue;
            }
            if (char.IsWhiteSpace(ch))
            {
                FlushChunk();
            }
            else
            {
                current.Append(ch);
            }
        }
        FlushChunk();
        return tokens;
    }

    // MARK: - Curve sampling

    /// <summary>Approximate a cubic Bezier with <paramref name="segments"/> line segments (16 by default).</summary>
    private static List<VectorPoint> SampleCubic(
        VectorPoint start, VectorPoint cp1, VectorPoint cp2, VectorPoint end, int segments)
    {
        var points = new List<VectorPoint> { start };
        for (var i = 1; i <= segments; i++)
        {
            var t = (double)i / segments;
            var mt = 1.0 - t;
            var mt2 = mt * mt;
            var mt3 = mt2 * mt;
            var t2 = t * t;
            var t3 = t2 * t;
            var x = mt3 * start.X + 3 * mt2 * t * cp1.X + 3 * mt * t2 * cp2.X + t3 * end.X;
            var y = mt3 * start.Y + 3 * mt2 * t * cp1.Y + 3 * mt * t2 * cp2.Y + t3 * end.Y;
            points.Add(new VectorPoint(x, y));
        }
        return points;
    }

    private static bool TryNum(string value, out double d) =>
        double.TryParse(value, NumStyle, CultureInfo.InvariantCulture, out d);
}
