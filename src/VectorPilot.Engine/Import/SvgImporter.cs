using System.Globalization;
using System.Text.RegularExpressions;
using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Parses SVG path data and primitive elements into <see cref="VectorShape"/> values.
/// Ported from ShopPilotGeometry.SVGImporter (Swift).
/// </summary>
/// <remarks>
/// Supported path commands: M/m L/l H/h V/v C/c S/s Q/q T/t A/a Z/z with implicit
/// repeated coordinate pairs. Curves are approximated by sampling into polyline points.
/// Supported elements: path, rect, circle, ellipse, line, polyline, polygon.
/// The document viewBox is honored (scale + translate) so imports land correctly.
/// text/image elements are skipped.
/// </remarks>
public static class SvgImporter
{
    private const NumberStyles NumStyle = NumberStyles.Float;

    private static readonly Regex NumberRegex = new(
        @"[-+]?(?:[0-9]+\.?[0-9]*|\.[0-9]+)(?:[eE][-+]?[0-9]+)?",
        RegexOptions.Compiled);

    private static readonly Regex PathTokenRegex = new(
        @"([MmZzLlHhVvCcSsQqTtAa])([^MmZzLlHhVvCcSsQqTtAa]*)",
        RegexOptions.Compiled);

    private static readonly Regex DAttrRegex = new(
        @"d\s*=\s*([""'])(.*?)\1",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SvgRootRegex = new(
        @"<svg\b([^>]*)>",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Parse an SVG string and return shapes. Never throws.</summary>
    public static List<VectorShape> Parse(string content)
    {
        var shapes = new List<VectorShape>();
        if (content is null) return shapes;

        var transform = ParseViewBox(content, out _, out _);

        // Path elements (d attributes), then primitive elements (matches Swift order).
        foreach (var d in ExtractPaths(content))
        {
            shapes.AddRange(ParsePathData(d, transform));
        }
        shapes.AddRange(ParsePrimitives(content, transform));

        return shapes;
    }

    /// <summary>Parse a single SVG path <c>d</c> attribute string with no viewBox transform.</summary>
    public static List<VectorShape> ParsePathData(string dAttribute) =>
        ParsePathData(dAttribute, SvgTransform.Identity);

    /// <summary>Parse a single SVG path <c>d</c> attribute string, applying the given transform.</summary>
    public static List<VectorShape> ParsePathData(string dAttribute, SvgTransform transform)
    {
        var shapes = Execute(Tokenize(dAttribute));
        if (!transform.IsIdentity)
        {
            for (var i = 0; i < shapes.Count; i++) shapes[i] = ApplyTransform(shapes[i], transform);
        }
        return shapes;
    }

    // MARK: - Command execution

    private static List<VectorShape> Execute(List<(string Type, List<double> Values)> commands)
    {
        var shapes = new List<VectorShape>();
        var currentPath = new List<VectorPoint>();
        var currentPosition = VectorPoint.Zero;

        // Last cubic control point (cp2 of the most recent C/S) for S reflection.
        VectorPoint? lastCubicControl = null;
        // Last quadratic control point (cp of the most recent Q/T) for T reflection.
        VectorPoint? lastQuadControl = null;

        void Flush()
        {
            if (currentPath.Count >= 2) shapes.Add(CreateShape(currentPath));
            currentPath = new List<VectorPoint>();
        }

        foreach (var (type, values) in commands)
        {
            var relative = char.IsLower(type[0]);
            var index = 0;

            switch (type)
            {
                case "M":
                case "m":
                    Flush();
                    if (values.Count >= 2)
                    {
                        currentPosition = relative
                            ? new VectorPoint(currentPosition.X + values[0], currentPosition.Y + values[1])
                            : new VectorPoint(values[0], values[1]);
                        currentPath = new List<VectorPoint> { currentPosition };
                        index = 2;
                    }
                    // Remaining pairs are implicit lineto.
                    while (index + 1 < values.Count)
                    {
                        currentPosition = relative
                            ? new VectorPoint(currentPosition.X + values[index], currentPosition.Y + values[index + 1])
                            : new VectorPoint(values[index], values[index + 1]);
                        currentPath.Add(currentPosition);
                        index += 2;
                    }
                    lastCubicControl = null;
                    lastQuadControl = null;
                    break;

                case "L":
                case "l":
                    while (index + 1 < values.Count)
                    {
                        currentPosition = relative
                            ? new VectorPoint(currentPosition.X + values[index], currentPosition.Y + values[index + 1])
                            : new VectorPoint(values[index], values[index + 1]);
                        currentPath.Add(currentPosition);
                        index += 2;
                    }
                    lastCubicControl = null;
                    lastQuadControl = null;
                    break;

                case "H":
                case "h":
                    while (index < values.Count)
                    {
                        currentPosition = relative
                            ? new VectorPoint(currentPosition.X + values[index], currentPosition.Y)
                            : new VectorPoint(values[index], currentPosition.Y);
                        currentPath.Add(currentPosition);
                        index += 1;
                    }
                    lastCubicControl = null;
                    lastQuadControl = null;
                    break;

                case "V":
                case "v":
                    while (index < values.Count)
                    {
                        currentPosition = relative
                            ? new VectorPoint(currentPosition.X, currentPosition.Y + values[index])
                            : new VectorPoint(currentPosition.X, values[index]);
                        currentPath.Add(currentPosition);
                        index += 1;
                    }
                    lastCubicControl = null;
                    lastQuadControl = null;
                    break;

                case "C":
                case "c":
                    while (index + 5 < values.Count)
                    {
                        var cp1 = Point(relative, currentPosition, values, index);
                        var cp2 = Point(relative, currentPosition, values, index + 2);
                        var end = Point(relative, currentPosition, values, index + 4);
                        currentPath.AddRange(ApproximateCubicBezier(currentPosition, cp1, cp2, end, 16).Skip(1));
                        currentPosition = end;
                        lastCubicControl = cp2;
                        lastQuadControl = null;
                        index += 6;
                    }
                    break;

                case "S":
                case "s":
                    while (index + 3 < values.Count)
                    {
                        var cp2 = Point(relative, currentPosition, values, index);
                        var end = Point(relative, currentPosition, values, index + 2);
                        // cp1 reflects the previous cubic control point about the current position.
                        var cp1 = lastCubicControl is VectorPoint lc
                            ? new VectorPoint(2 * currentPosition.X - lc.X, 2 * currentPosition.Y - lc.Y)
                            : currentPosition;
                        currentPath.AddRange(ApproximateCubicBezier(currentPosition, cp1, cp2, end, 16).Skip(1));
                        currentPosition = end;
                        lastCubicControl = cp2;
                        lastQuadControl = null;
                        index += 4;
                    }
                    break;

                case "Q":
                case "q":
                    while (index + 3 < values.Count)
                    {
                        var cp = Point(relative, currentPosition, values, index);
                        var end = Point(relative, currentPosition, values, index + 2);
                        currentPath.AddRange(ApproximateQuadraticBezier(currentPosition, cp, end, 16).Skip(1));
                        currentPosition = end;
                        lastQuadControl = cp;
                        lastCubicControl = null;
                        index += 4;
                    }
                    break;

                case "T":
                case "t":
                    while (index + 1 < values.Count)
                    {
                        var end = Point(relative, currentPosition, values, index);
                        // cp reflects the previous quadratic control point about the current position.
                        var cp = lastQuadControl is VectorPoint lq
                            ? new VectorPoint(2 * currentPosition.X - lq.X, 2 * currentPosition.Y - lq.Y)
                            : currentPosition;
                        currentPath.AddRange(ApproximateQuadraticBezier(currentPosition, cp, end, 16).Skip(1));
                        currentPosition = end;
                        lastQuadControl = cp;
                        lastCubicControl = null;
                        index += 2;
                    }
                    break;

                case "A":
                case "a":
                    while (index + 6 < values.Count)
                    {
                        var rx = Math.Abs(values[index]);
                        var ry = Math.Abs(values[index + 1]);
                        var xAxisRotation = values[index + 2];
                        var largeArc = values[index + 3] > 0.5;
                        var sweep = values[index + 4] > 0.5;
                        var end = relative
                            ? new VectorPoint(currentPosition.X + values[index + 5], currentPosition.Y + values[index + 6])
                            : new VectorPoint(values[index + 5], values[index + 6]);

                        var approximated = ApproximateArc(
                            currentPosition, end, rx, ry, xAxisRotation, largeArc, sweep, 16);
                        currentPath.AddRange(approximated.Skip(1));
                        currentPosition = end;
                        lastCubicControl = null;
                        lastQuadControl = null;
                        index += 7;
                    }
                    break;

                case "Z":
                case "z":
                    if (currentPath.Count > 0)
                    {
                        var startPoint = currentPath[0];
                        if (Math.Abs(startPoint.X - currentPosition.X) > 1e-6
                            || Math.Abs(startPoint.Y - currentPosition.Y) > 1e-6)
                        {
                            currentPath.Add(startPoint);
                        }
                        // Per SVG spec, Z sets the current point back to the subpath start.
                        currentPosition = startPoint;
                    }
                    lastCubicControl = null;
                    lastQuadControl = null;
                    break;
            }
        }

        Flush();
        return shapes;
    }

    private static VectorPoint Point(bool relative, VectorPoint basePt, List<double> values, int index) =>
        relative
            ? new VectorPoint(basePt.X + values[index], basePt.Y + values[index + 1])
            : new VectorPoint(values[index], values[index + 1]);

    // MARK: - Shape creation

    /// <summary>Create a VectorShape from an array of points (mirrors Swift createShape).</summary>
    private static VectorShape CreateShape(List<VectorPoint> pathPoints)
    {
        var first = pathPoints[0];
        var last = pathPoints[^1];
        var isClosed = Math.Abs(first.X - last.X) < 1e-6 && Math.Abs(first.Y - last.Y) < 1e-6;

        if (isClosed && pathPoints.Count >= 5)
        {
            // Check if it looks like an axis-aligned rectangle: every consecutive
            // triple has perpendicular, non-degenerate segments (right-angle corners).
            var isRect = true;
            for (var k = 1; k < pathPoints.Count - 2; k++)
            {
                var prev = pathPoints[k - 1];
                var curr = pathPoints[k];
                var next = pathPoints[k + 1];

                var dx1 = curr.X - prev.X;
                var dy1 = curr.Y - prev.Y;
                var dx2 = next.X - curr.X;
                var dy2 = next.Y - curr.Y;

                var dot = dx1 * dx2 + dy1 * dy2;
                var degenerate = (Math.Abs(dx1) < 1e-9 && Math.Abs(dy1) < 1e-9)
                    || (Math.Abs(dx2) < 1e-9 && Math.Abs(dy2) < 1e-9);
                if (Math.Abs(dot) > 1e-3 || degenerate)
                {
                    isRect = false;
                    break;
                }
            }

            if (isRect)
            {
                // Robust to any starting corner: use the path bounds.
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                foreach (var p in pathPoints)
                {
                    minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                    minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
                }
                return VectorShape.Rectangle(minX, minY, maxX - minX, maxY - minY);
            }
        }

        if (isClosed && pathPoints.Count >= 4)
        {
            var points = new List<VectorPoint>(pathPoints);
            points.RemoveAt(points.Count - 1);
            return VectorShape.Polyline(points, closed: true);
        }

        if (pathPoints.Count == 2)
            return VectorShape.Line(pathPoints[0], pathPoints[1]);

        return VectorShape.Polyline(pathPoints, closed: false);
    }

    // MARK: - Curve approximation

    /// <summary>Approximate a cubic Bezier curve with line segments.</summary>
    private static List<VectorPoint> ApproximateCubicBezier(
        VectorPoint start, VectorPoint cp1, VectorPoint cp2, VectorPoint end, int segments = 16)
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

    /// <summary>Approximate a quadratic Bezier curve with line segments.</summary>
    private static List<VectorPoint> ApproximateQuadraticBezier(
        VectorPoint start, VectorPoint cp, VectorPoint end, int segments = 16)
    {
        var points = new List<VectorPoint> { start };
        for (var i = 1; i <= segments; i++)
        {
            var t = (double)i / segments;
            var mt = 1.0 - t;
            var x = mt * mt * start.X + 2 * mt * t * cp.X + t * t * end.X;
            var y = mt * mt * start.Y + 2 * mt * t * cp.Y + t * t * end.Y;
            points.Add(new VectorPoint(x, y));
        }
        return points;
    }

    /// <summary>Approximate an SVG arc with line segments (ports the Swift endpoint-to-center conversion).</summary>
    private static List<VectorPoint> ApproximateArc(
        VectorPoint start, VectorPoint end,
        double radiusX, double radiusY, double xAxisRotation,
        bool largeArc, bool sweep, int segments = 16)
    {
        if (radiusX <= 1e-6 || radiusY <= 1e-6) return new List<VectorPoint> { start, end };

        var midX = (start.X + end.X) / 2.0;
        var midY = (start.Y + end.Y) / 2.0;

        var dx = (start.X - end.X) / 2.0;
        var dy = (start.Y - end.Y) / 2.0;

        var xAngle = xAxisRotation * Math.PI / 180.0;
        var cosX = Math.Cos(xAngle);
        var sinX = Math.Sin(xAngle);

        var x1p = cosX * dx + sinX * dy;
        var y1p = -sinX * dx + cosX * dy;

        var localRx = radiusX;
        var localRy = radiusY;

        var rxSq = localRx * localRx;
        var rySq = localRy * localRy;
        var x1pSq = x1p * x1p;
        var y1pSq = y1p * y1p;

        var lambda = x1pSq / rxSq + y1pSq / rySq;
        if (lambda > 1.0)
        {
            var scaleFactor = Math.Sqrt(lambda);
            localRx *= scaleFactor;
            localRy *= scaleFactor;
        }

        var srxSq = localRx * localRx;
        var srySq = localRy * localRy;

        var sign = largeArc == sweep ? -1.0 : 1.0;
        var sq = (srxSq * srySq - srxSq * y1pSq - srySq * x1pSq) / (srxSq * y1pSq + srySq * x1pSq);
        var coef = sign * Math.Sqrt(Math.Max(0, sq));
        var cxp = coef * (localRy * x1p / localRx);
        var cyp = -coef * (localRx * y1p / localRy);

        var cx = cosX * cxp - sinX * cyp + midX;
        var cy = sinX * cxp + cosX * cyp + midY;

        var ux = (x1p - cxp) / localRx;
        var uy = (y1p - cyp) / localRy;
        var vx = (-x1p - cxp) / localRx;
        var vy = (-y1p - cyp) / localRy;

        var startAngle = Math.Atan2(uy, ux);
        var endAngle = Math.Atan2(vy, vx);

        if (sweep && startAngle > endAngle) endAngle -= 2 * Math.PI;
        else if (!sweep && endAngle > startAngle) endAngle += 2 * Math.PI;

        var points = new List<VectorPoint> { start };
        var angleDiff = endAngle - startAngle;

        for (var i = 1; i <= segments; i++)
        {
            var t = (double)i / segments;
            var angle = startAngle + angleDiff * t;
            var x = cx + localRx * Math.Cos(angle) * cosX - localRy * Math.Sin(angle) * sinX;
            var y = cy + localRx * Math.Cos(angle) * sinX + localRy * Math.Sin(angle) * cosX;
            points.Add(new VectorPoint(x, y));
        }

        return points;
    }

    // MARK: - Transform

    /// <summary>Affine transform derived from an SVG viewBox (scale + translate).</summary>
    public readonly record struct SvgTransform(double ScaleX, double ScaleY, double OffsetX, double OffsetY)
    {
        public static readonly SvgTransform Identity = new(1, 1, 0, 0);

        public bool IsIdentity =>
            Math.Abs(ScaleX - 1) < 1e-9 && Math.Abs(ScaleY - 1) < 1e-9
            && Math.Abs(OffsetX) < 1e-9 && Math.Abs(OffsetY) < 1e-9;

        /// <summary>Average scale factor, used for radii when scaling is (near-)uniform.</summary>
        public double RadiusScale => (Math.Abs(ScaleX) + Math.Abs(ScaleY)) / 2;

        public VectorPoint Apply(VectorPoint p) => new(p.X * ScaleX + OffsetX, p.Y * ScaleY + OffsetY);
    }

    /// <summary>Apply a viewBox transform to every point of a shape (radii scale by the average factor).</summary>
    private static VectorShape ApplyTransform(VectorShape shape, SvgTransform t)
    {
        switch (shape.Type)
        {
            case ShapeType.Line:
                return VectorShape.Line(t.Apply(shape.Points[0]), t.Apply(shape.Points[1]));

            case ShapeType.Rectangle:
            {
                var b = shape.Bounds();
                return VectorShape.Rectangle(
                    b.MinX * t.ScaleX + t.OffsetX,
                    b.MinY * t.ScaleY + t.OffsetY,
                    b.Width * t.ScaleX,
                    b.Height * t.ScaleY);
            }

            case ShapeType.Circle:
                return VectorShape.Circle(t.Apply(shape.Points[0]), shape.Radius * t.RadiusScale);

            case ShapeType.Arc:
            {
                var arc = new VectorShape
                {
                    Type = ShapeType.Arc,
                    Radius = shape.Radius * t.RadiusScale,
                    StartAngleDeg = shape.StartAngleDeg,
                    EndAngleDeg = shape.EndAngleDeg
                };
                arc.Points.Add(t.Apply(shape.Points[0]));
                return arc;
            }

            default:
                return VectorShape.Polyline(shape.Points.Select(t.Apply), shape.Closed);
        }
    }

    // MARK: - Element extraction

    /// <summary>Extract path d attribute values from an SVG string.</summary>
    private static List<string> ExtractPaths(string svgString)
    {
        var paths = new List<string>();
        foreach (Match match in DAttrRegex.Matches(svgString))
        {
            paths.Add(match.Groups[2].Value);
        }
        return paths;
    }

    /// <summary>Parse the root &lt;svg&gt; viewBox (and width/height) into a transform.</summary>
    private static SvgTransform ParseViewBox(string svgString, out double? width, out double? height)
    {
        var match = SvgRootRegex.Match(svgString);
        if (!match.Success)
        {
            width = null;
            height = null;
            return SvgTransform.Identity;
        }

        var attrs = match.Groups[1].Value;
        width = NumberAttr("width", attrs);
        height = NumberAttr("height", attrs);

        var viewBoxString = StringAttr("viewBox", attrs);
        if (viewBoxString is null) return SvgTransform.Identity;

        var numbers = Numbers(viewBoxString);
        if (numbers.Count < 4 || numbers[2] <= 1e-9 || numbers[3] <= 1e-9)
            return SvgTransform.Identity;

        var minX = numbers[0];
        var minY = numbers[1];
        var viewBoxWidth = numbers[2];
        var viewBoxHeight = numbers[3];
        var scaleX = (width ?? viewBoxWidth) / viewBoxWidth;
        var scaleY = (height ?? viewBoxHeight) / viewBoxHeight;
        return new SvgTransform(scaleX, scaleY, -minX * scaleX, -minY * scaleY);
    }

    /// <summary>Extract primitive elements (rect/circle/ellipse/line/polyline/polygon) into shapes.</summary>
    private static List<VectorShape> ParsePrimitives(string svgString, SvgTransform t)
    {
        var shapes = new List<VectorShape>();

        // <rect x y width height (rx ry)>
        foreach (var attrs in ElementAttributes("rect", svgString))
        {
            var w = NumberAttr("width", attrs);
            var h = NumberAttr("height", attrs);
            if (w is null || h is null) continue;
            var x = NumberAttr("x", attrs) ?? 0;
            var y = NumberAttr("y", attrs) ?? 0;
            var origin = t.Apply(new VectorPoint(x, y));
            var scaledWidth = w.Value * t.ScaleX;
            var scaledHeight = h.Value * t.ScaleY;
            var minX = Math.Min(origin.X, origin.X + scaledWidth);
            var minY = Math.Min(origin.Y, origin.Y + scaledHeight);
            shapes.Add(VectorShape.Rectangle(minX, minY, Math.Abs(scaledWidth), Math.Abs(scaledHeight)));
        }

        // <circle cx cy r>
        foreach (var attrs in ElementAttributes("circle", svgString))
        {
            var r = NumberAttr("r", attrs);
            if (r is null) continue;
            var center = t.Apply(PointAttr("cx", "cy", attrs));
            shapes.Add(VectorShape.Circle(center, r.Value * t.RadiusScale));
        }

        // <ellipse cx cy rx ry> — approximated as a closed polyline (VectorShape has no rx/ry fields)
        foreach (var attrs in ElementAttributes("ellipse", svgString))
        {
            var rx = NumberAttr("rx", attrs);
            var ry = NumberAttr("ry", attrs);
            if (rx is null || ry is null) continue;
            var center = t.Apply(PointAttr("cx", "cy", attrs));
            var ex = Math.Abs(rx.Value * t.ScaleX);
            var ey = Math.Abs(ry.Value * t.ScaleY);
            var pts = new List<VectorPoint>();
            for (var k = 0; k < 64; k++)
            {
                var a = 2 * Math.PI * k / 64;
                pts.Add(new VectorPoint(center.X + ex * Math.Cos(a), center.Y + ey * Math.Sin(a)));
            }
            shapes.Add(VectorShape.Polyline(pts, closed: true));
        }

        // <line x1 y1 x2 y2>
        foreach (var attrs in ElementAttributes("line", svgString))
        {
            var x1 = NumberAttr("x1", attrs);
            var y1 = NumberAttr("y1", attrs);
            var x2 = NumberAttr("x2", attrs);
            var y2 = NumberAttr("y2", attrs);
            if (x1 is null || y1 is null || x2 is null || y2 is null) continue;
            shapes.Add(VectorShape.Line(
                t.Apply(new VectorPoint(x1.Value, y1.Value)),
                t.Apply(new VectorPoint(x2.Value, y2.Value))));
        }

        // <polyline points="x,y x,y ...">
        foreach (var attrs in ElementAttributes("polyline", svgString))
        {
            var pointsString = StringAttr("points", attrs);
            if (pointsString is null) continue;
            var pairs = NumberPairs(pointsString);
            if (pairs.Count < 2) continue;
            shapes.Add(VectorShape.Polyline(pairs.Select(t.Apply), closed: false));
        }

        // <polygon points="x,y x,y ..."> — closed
        foreach (var attrs in ElementAttributes("polygon", svgString))
        {
            var pointsString = StringAttr("points", attrs);
            if (pointsString is null) continue;
            var pairs = NumberPairs(pointsString);
            if (pairs.Count < 3) continue;
            var points = pairs.Select(t.Apply).ToList();
            if (Math.Abs(points[0].X - points[^1].X) > 1e-6 || Math.Abs(points[0].Y - points[^1].Y) > 1e-6)
                points.Add(points[0]);
            shapes.Add(VectorShape.Polyline(points, closed: true));
        }

        return shapes;
    }

    /// <summary>Capture the attribute string of every occurrence of the given element.</summary>
    private static List<string> ElementAttributes(string element, string svgString)
    {
        var pattern = $@"<{element}\b([^>]*)>";
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);
        var results = new List<string>();
        foreach (Match match in regex.Matches(svgString))
        {
            results.Add(match.Groups[1].Value.Trim());
        }
        return results;
    }

    private static string? StringAttr(string name, string attrs)
    {
        var pattern = $@"{name}\s*=\s*([""'])(.*?)\1";
        var regex = new Regex(pattern, RegexOptions.IgnoreCase);
        var match = regex.Match(attrs);
        return match.Success ? match.Groups[2].Value : null;
    }

    private static double? NumberAttr(string name, string attrs)
    {
        var value = StringAttr(name, attrs);
        return value is not null && TryNum(value, out var d) ? d : null;
    }

    private static VectorPoint PointAttr(string cx, string cy, string attrs) =>
        new(NumberAttr(cx, attrs) ?? 0, NumberAttr(cy, attrs) ?? 0);

    /// <summary>Parse a whitespace/comma-separated number list into pairs of points.</summary>
    private static List<VectorPoint> NumberPairs(string s)
    {
        var values = Numbers(s);
        var points = new List<VectorPoint>();
        var index = 0;
        while (index + 1 < values.Count)
        {
            points.Add(new VectorPoint(values[index], values[index + 1]));
            index += 2;
        }
        return points;
    }

    /// <summary>Parse a whitespace/comma-separated number list.</summary>
    private static List<double> Numbers(string s)
    {
        var result = new List<double>();
        foreach (Match match in NumberRegex.Matches(s))
        {
            if (TryNum(match.Value, out var d)) result.Add(d);
        }
        return result;
    }

    // MARK: - Tokenization

    /// <summary>Tokenize an SVG path d attribute into commands.</summary>
    private static List<(string Type, List<double> Values)> Tokenize(string dAttribute)
    {
        var commands = new List<(string, List<double>)>();
        var trimmed = dAttribute.Trim();
        if (trimmed.Length == 0) return commands;

        foreach (Match match in PathTokenRegex.Matches(trimmed))
        {
            var commandType = match.Groups[1].Value;
            var argsString = match.Groups[2].Value.Trim();
            var values = new List<double>();
            if (argsString.Length > 0)
            {
                foreach (Match numMatch in NumberRegex.Matches(argsString))
                {
                    if (TryNum(numMatch.Value, out var value)) values.Add(value);
                }
            }
            commands.Add((commandType, values));
        }
        return commands;
    }

    private static bool TryNum(string value, out double d) =>
        double.TryParse(value, NumStyle, CultureInfo.InvariantCulture, out d);
}
