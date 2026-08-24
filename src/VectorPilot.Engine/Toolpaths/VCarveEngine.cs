using System.Globalization;
using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>V-Carve strategy parameters (ported from VCarveParams.swift, SPK-1136d + SPK-VCarveClear).</summary>
public sealed class VCarveParams
{
    public double VBitAngleDegrees { get; set; } = 90.0;
    public double FeedRateMmPerMin { get; set; } = 1000;
    public double PlungeFeedRateMmPerMin { get; set; } = 300;
    public double MaxDepthOfCutMm { get; set; } = 2.0;
    public double LeadInDistanceMm { get; set; } = 5.0;
    public double LeadOutDistanceMm { get; set; } = 5.0;
    public double StepOverMm { get; set; } = 1.0;
    public bool FlatBottomMode { get; set; }

    /// <summary>
    /// Cut the shape's medial axis (skeleton) as well as its outline.
    ///
    /// Without this the engine only samples depth ALONG THE INPUT PATH, so the middle of a
    /// closed shape is never visited — a V-carve of a wide letter or a dumbbell leaves the
    /// deepest region uncut. On by default.
    /// </summary>
    public bool MedialAxisPass { get; set; } = true;

    /// <summary>Clearance-field grid resolution for the skeleton (mm).</summary>
    public double MedialAxisCellMm { get; set; } = 1.0;
    public Dictionary<Guid, double> VectorDepths { get; set; } = new();
    public double StartDepthMm { get; set; }
    public double FlatDepthMm { get; set; } = 1.0;
    public bool CornerSharpen { get; set; }
    public bool UseVectorStartPoints { get; set; } = true;
    public bool UseVectorSelectionOrder { get; set; }
    public double SafeZHeightMm { get; set; } = 3.2;
    public bool RampPlungeMoves { get; set; }
    public bool ClearancePassEnabled { get; set; }
    public double ClearanceToolDiameterMm { get; set; } = 6.0;
    public double ClearanceDepthMm { get; set; } = 1.0;
    public double ClearanceStepOverMm { get; set; } = 0.4;
    public double SpindleRpm { get; set; }

    /// <summary>
    /// P-202: clear flat areas the V-bit physically cannot reach. Where a shape's
    /// half-width exceeds the depth-limited tip width, a V-bit bottoms out at
    /// MaxDepthOfCutMm and leaves uncut stock on either side of the spine. With
    /// this flag the medial-axis ridge in those too-wide regions gets a second,
    /// offset sweep at full depth (endmill-clear style), so wide slots and fat
    /// letterforms bottom out flat instead of leaving a ridge of stock.
    /// </summary>
    public bool FlatAreaClearing { get; set; }

    /// <summary>How far beyond the V-bit's reachable width a region must be before
    /// the flat-clearing sweep visits it, as a fraction of TipWidthAtDepth.</summary>
    public double FlatAreaThresholdFactor { get; set; } = 1.5;

    /// <summary>Sweep line spacing inside flat regions, in mm.</summary>
    public double FlatAreaStepOverMm { get; set; } = 1.0;

    /// <summary>Half-angle of the V-bit in radians.</summary>
    public double HalfAngleRadians => (Math.PI / 180.0 * VBitAngleDegrees) / 2.0;

    /// <summary>Tip width at a given depth: 2·|z|·tan(halfAngle).</summary>
    public double TipWidthAtDepth(double depth) => 2.0 * Math.Abs(depth) * Math.Tan(HalfAngleRadians);
}

/// <summary>Computed V-carve result.</summary>
public sealed class VCarveResult
{
    public VCarveParams Params { get; init; } = new();
    public List<string> GcodeLines { get; init; } = new();
    public double EstimatedTimeSeconds { get; init; }
    public int PassCount { get; init; }
    public double? BoundsMinX { get; init; }
    public double? BoundsMinY { get; init; }
    public double? BoundsMaxX { get; init; }
    public double? BoundsMaxY { get; init; }
}

/// <summary>
/// V-carve toolpath engine (ported from VCarveEngine.swift): per-vector Z-depth maps
/// to the V-bit's cutting width, multi-pass with Y-based shading, flat-bottom mode,
/// lead-in/out, optional clearance pass. Units: millimetres (G21), faithful to the Swift.
/// </summary>
public static class VCarveEngine
{
    public static VCarveResult Compute(IReadOnlyList<VectorShape> vectors, VCarveParams params_, double stockHeightMm = 25.0)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (var v in vectors)
        {
            foreach (var p in v.Points)
            {
                minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
                minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
            }
        }
        bool hasBounds = vectors.Any(v => v.Points.Count > 0);

        // Per-vector Y range for shading.
        var vectorBounds = new Dictionary<Guid, (double MinY, double MaxY)>();
        foreach (var v in vectors)
        {
            if (v.Points.Count == 0) continue;
            vectorBounds[v.Id] = (v.Points.Min(p => p.Y), v.Points.Max(p => p.Y));
        }

        var g = new List<string>();
        double feed = params_.FeedRateMmPerMin;
        double plunge = params_.PlungeFeedRateMmPerMin;

        g.Add("%");
        if (params_.SpindleRpm > 0) g.Add($"M3 S{(int)params_.SpindleRpm}");
        if (params_.ClearancePassEnabled && hasBounds)
        {
            g.AddRange(ClearanceGcode(vectors, params_, (minX, minY, maxX, maxY)));
        }
        g.Add("O=V_CARVE_TOOLPATH");
        g.Add($"(V-Bit: {(int)params_.VBitAngleDegrees}°)");
        g.Add($"(Flat Bottom: {(params_.FlatBottomMode ? "Yes" : "No")})");

        double totalCuttingLength = 0;
        int maxPassCount = 0;

        foreach (var vector in vectors)
        {
            if (vector.Points.Count < 2) continue;

            double maxDepth = params_.VectorDepths.TryGetValue(vector.Id, out var d) ? d : params_.MaxDepthOfCutMm;
            double tipWidth = params_.TipWidthAtDepth(maxDepth);
            int passCount = Math.Max(1, (int)Math.Ceiling(tipWidth / Math.Max(params_.StepOverMm, 1e-9)));
            maxPassCount = Math.Max(maxPassCount, passCount);

            var (vecMinY, vecMaxY) = vectorBounds.TryGetValue(vector.Id, out var vb) ? vb : (0.0, 1.0);
            double yRange = vecMaxY - vecMinY;

            for (int pass = 1; pass <= passCount; pass++)
            {
                double depthFactor = (double)pass / passCount;
                double zDepth = -maxDepth * depthFactor;
                double actualZ = params_.FlatBottomMode ? -maxDepth : zDepth;

                g.Add("");
                g.Add($"(Pass {pass}/{passCount}, Z={F3(actualZ)})");
                g.Add("G0 Z5.0");

                var start = vector.Points[0];
                // Depth at the FIRST point must come from the local width too. Plunging
                // to the pass depth meant a 2mm slot and a 12mm channel both bottomed
                // out at the depth limit — width drove the middle of the cut but not
                // its entry, so the deepest Z in the program was always the clamp.
                double startHalfWidth = VCarveGeometry.DistanceToNearestOtherEdge(vector, 0, vectors);
                double startZ = Math.Max(
                    VCarveGeometry.DepthForHalfWidth(startHalfWidth, params_.VBitAngleDegrees, maxDepth),
                    actualZ);

                double leadInX = start.X - params_.LeadInDistanceMm;
                g.Add($"G0 X{F3(leadInX)} Y{F3(start.Y)}");
                g.Add($"G1 Z{F3(startZ)} F{(int)plunge}");
                g.Add($"G1 X{F3(start.X)} Y{F3(start.Y)} F{(int)feed}");

                double lastZ = startZ;
                for (int i = 1; i < vector.Points.Count; i++)
                {
                    var p = vector.Points[i];
                    // Depth from the LOCAL CHANNEL WIDTH (medial-axis distance), not
                    // from Y position on the page. A V-bit can only sink as deep as
                    // the available width allows: z = -(halfWidth / tan(halfAngle)).
                    double halfWidth = VCarveGeometry.DistanceToNearestOtherEdge(vector, i, vectors);
                    double z = VCarveGeometry.DepthForHalfWidth(halfWidth, params_.VBitAngleDegrees, maxDepth);
                    z = Math.Max(z, actualZ);   // never exceed this pass's depth
                    lastZ = z;
                    g.Add($"G1 X{F3(p.X)} Y{F3(p.Y)} Z{F3(z)} F{(int)feed}");
                }

                if (vector.Closed && vector.Points.Count > 2)
                {
                    var first = vector.Points[0];
                    g.Add($"G1 X{F3(first.X)} Y{F3(first.Y)} Z{F3(startZ)} F{(int)feed}");
                }

                var end = vector.Points[^1];
                // Lead out at the depth the cut actually ended on, not the pass clamp.
                g.Add($"G1 X{F3(end.X + params_.LeadOutDistanceMm)} Y{F3(end.Y)} Z{F3(lastZ)} F{(int)feed}");
                g.Add("G0 Z5.0");
            }

            totalCuttingLength += PathLength(vector.Points);

            // ---- medial-axis (skeleton) pass ----
            //
            // Tracing the outline alone leaves the middle of a closed shape uncut: a
            // V-bit must plunge deepest along the SPINE, where the shape is widest.
            // Sampling depth along the input path — which is all this engine used to do —
            // can never reach it.
            if (params_.MedialAxisPass && vector.Closed && vector.Points.Count >= 3)
            {
                var skeleton = MedialAxis.Compute(vector.Points, params_.MedialAxisCellMm);
                if (skeleton.IsEmpty) continue;

                g.Add("");
                g.Add($"(Medial axis: {skeleton.Paths.Count} ridge path(s), " +
                      $"max clearance {F3(skeleton.MaxClearanceMm)}mm)");

                foreach (var path in skeleton.Paths)
                {
                    if (path.Count < 2) continue;

                    g.Add("G0 Z5.0");

                    var head = path[0];
                    // DepthForHalfWidth already returns a NEGATIVE, maxDepth-clamped Z.
                    // Negating it again put the cutter ABOVE the stock (Z+6), cutting air.
                    double headZ = VCarveGeometry.DepthForHalfWidth(
                        head.ClearanceMm, params_.VBitAngleDegrees, maxDepth);

                    g.Add($"G0 X{F3(head.Position.X)} Y{F3(head.Position.Y)}");
                    g.Add($"G1 Z{F3(headZ)} F{(int)plunge}");

                    for (int i = 1; i < path.Count; i++)
                    {
                        var pt = path[i];
                        // Depth follows the LOCAL half-width: wide spine = deeper cut.
                        double z = VCarveGeometry.DepthForHalfWidth(
                            pt.ClearanceMm, params_.VBitAngleDegrees, maxDepth);
                        g.Add($"G1 X{F3(pt.Position.X)} Y{F3(pt.Position.Y)} Z{F3(z)} F{(int)feed}");
                    }

                    g.Add("G0 Z5.0");
                    totalCuttingLength += PathLength(path.Select(p => p.Position).ToList());
                }

                // ---- P-202: flat-area clearing ----
                //
                // Where the ridge's clearance exceeds what a V-bit can widen to by
                // MaxDepthOfCutMm, the bit bottoms out and stock remains on both
                // sides. Sweep the too-wide ridge segments laterally at full depth,
                // stepping across the flat width — endmill-clear style.
                if (params_.FlatAreaClearing)
                {
                    g.AddRange(FlatAreaSweep(skeleton, vector, params_, maxDepth));
                }
            }
        }

        g.Add("");
        g.Add("M30");
        g.Add("%");

        double cuttingTime = totalCuttingLength * maxPassCount / Math.Max(feed, 1e-9) * 60.0;

        return new VCarveResult
        {
            Params = params_,
            GcodeLines = g,
            EstimatedTimeSeconds = cuttingTime,
            PassCount = maxPassCount,
            BoundsMinX = hasBounds ? minX : null,
            BoundsMinY = hasBounds ? minY : null,
            BoundsMaxX = hasBounds ? maxX : null,
            BoundsMaxY = hasBounds ? maxY : null
        };
    }

    /// <summary>
    /// P-202: sweep the too-wide segments of the medial-axis ridge at full depth.
    /// A ridge point is "flat" when its clearance exceeds the V-bit's reachable
    /// half-width (TipWidthAtDepth/2) by the threshold factor. For each flat run,
    /// lateral passes step across the flat width on BOTH sides of the spine at
    /// -maxDepth, so the fat region bottoms out instead of keeping a stock ridge.
    /// </summary>
    internal static List<string> FlatAreaSweep(
        MedialAxis.Result skeleton, VectorShape vector, VCarveParams p, double maxDepth)
    {
        var g = new List<string>();
        string F3(double v) => v.ToString("F3", CultureInfo.InvariantCulture);

        double tipHalf = p.TipWidthAtDepth(maxDepth) / 2.0;
        double threshold = Math.Max(tipHalf * p.FlatAreaThresholdFactor, tipHalf + 1e-6);
        double zFlat = -maxDepth;
        double feed = (int)p.FeedRateMmPerMin;
        double plunge = (int)p.PlungeFeedRateMmPerMin;

        foreach (var path in skeleton.Paths)
        {
            // Split the ridge into maximal runs where clearance >= threshold.
            int i = 0;
            while (i < path.Count)
            {
                if (path[i].ClearanceMm < threshold) { i++; continue; }
                int j = i;
                while (j + 1 < path.Count && path[j + 1].ClearanceMm >= threshold) j++;

                // Flat run [i..j]: sweep laterally. The extra half-width each side
                // of the spine that the V-bit cannot reach:
                double extra = path[i].ClearanceMm - tipHalf;
                if (extra > 1e-6 && path[i].Position.DistanceTo(path[j].Position) > 1e-6)
                {
                    int sweeps = Math.Max(1, (int)Math.Ceiling(extra * 2 / Math.Max(p.FlatAreaStepOverMm, 0.05)));
                    // Lateral direction: perpendicular to the run's overall direction.
                    double dx = path[j].Position.X - path[i].Position.X;
                    double dy = path[j].Position.Y - path[i].Position.Y;
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    if (len > 1e-9)
                    {
                        double px = -dy / len, py = dx / len;   // unit perpendicular

                        for (int sIdx = 0; sIdx <= sweeps; sIdx++)
                        {
                            // Offsets straddle the spine: 0, +step, −step, +2·step, …
                            double off = ((sIdx + 1) / 2) * (sIdx % 2 == 1 ? 1 : -1)
                                         * p.FlatAreaStepOverMm;
                            if (Math.Abs(off) > extra) continue;   // stay inside the flat band

                            g.Add("G0 Z5.0");
                            var a = path[i].Position;
                            var b = path[j].Position;
                            g.Add($"G0 X{F3(a.X + px * off)} Y{F3(a.Y + py * off)}");
                            g.Add($"G1 Z{F3(zFlat)} F{(int)plunge}");
                            g.Add($"G1 X{F3(b.X + px * off)} Y{F3(b.Y + py * off)} F{(int)feed}");
                            g.Add("G0 Z5.0");
                        }
                    }
                }

                i = j + 1;
            }
        }

        if (g.Count > 0)
            g.Insert(0, $"(Flat area clearing: regions wider than {F3(tipHalf * 2)}mm tip width)");

        return g;
    }

    /// <summary>
    /// Clearance pass: a flat end mill raster-clears the wide open bands inside the
    /// vectors' bounding box (excluding tool-radius margins around each vector) down
    /// to clearanceDepthMm, before the V-bit detail pass. Ported from VCarveEngine.swift.
    /// </summary>
    private static List<string> ClearanceGcode(IReadOnlyList<VectorShape> vectors, VCarveParams p,
        (double MinX, double MinY, double MaxX, double MaxY) bounds)
    {
        double toolR = p.ClearanceToolDiameterMm / 2.0;
        double step = p.ClearanceStepOverMm * p.ClearanceToolDiameterMm;
        double margin = toolR + 1.0;
        if (toolR <= 1e-9 || step <= 1e-9 ||
            bounds.MaxX - bounds.MinX <= 2 * toolR || bounds.MaxY - bounds.MinY <= 2 * toolR)
        {
            return new List<string>();
        }

        var strictlyInside = vectors.Where(v =>
        {
            if (v.Points.Count == 0) return false;
            double vMinX = v.Points.Min(pt => pt.X), vMaxX = v.Points.Max(pt => pt.X);
            double vMinY = v.Points.Min(pt => pt.Y), vMaxY = v.Points.Max(pt => pt.Y);
            return vMinX > bounds.MinX + 1e-6 && vMaxX < bounds.MaxX - 1e-6 &&
                   vMinY > bounds.MinY + 1e-6 && vMaxY < bounds.MaxY - 1e-6;
        }).ToList();
        bool protectAll = strictlyInside.Count == 0;

        var exclusions = new List<(double MinX, double MaxX, double MinY, double MaxY)>();
        foreach (var v in vectors)
        {
            if (v.Points.Count == 0) continue;
            double vMinX = v.Points.Min(pt => pt.X), vMaxX = v.Points.Max(pt => pt.X);
            double vMinY = v.Points.Min(pt => pt.Y), vMaxY = v.Points.Max(pt => pt.Y);
            bool isInside = vMinX > bounds.MinX + 1e-6 && vMaxX < bounds.MaxX - 1e-6 &&
                            vMinY > bounds.MinY + 1e-6 && vMaxY < bounds.MaxY - 1e-6;
            if (!protectAll && !isInside) continue;
            exclusions.Add((vMinX - margin, vMaxX + margin, vMinY, vMaxY));
        }

        double depth = -p.ClearanceDepthMm;
        var lines = new List<string>
        {
            "",
            "O=VCARVE_CLEARANCE",
            $"(Clearance tool: {F1(p.ClearanceToolDiameterMm)}mm)",
            $"(Clearance depth: {F2(p.ClearanceDepthMm)}mm)"
        };

        double y = bounds.MinY + toolR;
        bool leftToRight = true;
        while (y <= bounds.MaxY - toolR)
        {
            var rowBands = exclusions
                .Where(e => y >= e.MinY - toolR && y <= e.MaxY + toolR)
                .OrderBy(e => e.MinX)
                .ToList();
            var gaps = new List<(double X0, double X1)>();
            double cursor = bounds.MinX + toolR;
            foreach (var band in rowBands)
            {
                double bandStart = Math.Max(cursor, band.MinX);
                if (bandStart < bounds.MaxX - toolR && bandStart > cursor + 1e-6)
                {
                    gaps.Add((cursor, Math.Min(bandStart, bounds.MaxX - toolR)));
                }
                cursor = Math.Max(cursor, band.MaxX);
                if (cursor >= bounds.MaxX - toolR) break;
            }
            if (cursor < bounds.MaxX - toolR - 1e-6)
            {
                gaps.Add((cursor, bounds.MaxX - toolR));
            }

            foreach (var gap in gaps)
            {
                double x0 = leftToRight ? gap.X0 : gap.X1;
                double x1 = leftToRight ? gap.X1 : gap.X0;
                lines.Add("G0 Z5.0");
                lines.Add($"G0 X{F3(x0)} Y{F3(y)}");
                lines.Add($"G1 Z{F3(depth)} F{(int)p.PlungeFeedRateMmPerMin}");
                lines.Add($"G1 X{F3(x1)} Y{F3(y)} F{(int)p.FeedRateMmPerMin}");
                leftToRight = !leftToRight;
            }
            y += step;
        }
        return lines;
    }

    private static double PathLength(IReadOnlyList<VectorPoint> pts)
    {
        double len = 0;
        for (int i = 1; i < pts.Count; i++) len += pts[i - 1].DistanceTo(pts[i]);
        return len;
    }

    private static string F3(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);
    private static string F2(double v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    private static string F1(double v) => v.ToString("0.0", CultureInfo.InvariantCulture);
}
