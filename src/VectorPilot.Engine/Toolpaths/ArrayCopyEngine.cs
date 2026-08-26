using System.Globalization;
using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>Kind of array copy (ported from Swift ArrayCopyType / ArrayCopyPattern).</summary>
public enum ArrayCopyType
{
    Linear,
    Circular,
    Grid
}

/// <summary>
/// Linear array copy parameters (ported from Swift LinearPattern / LinearArrayCopyParams).
/// Copies are laid out along a direction vector; <see cref="AngleDeg"/> is the direction of
/// the array (degrees, normalized 0..360, 0 = +X). With <see cref="RotaryAxis"/> set, the
/// spacing is applied to the A (rotary) axis instead of X/Y.
/// </summary>
public sealed class LinearPattern
{
    public int Count { get; set; } = 2;
    public double SpacingMm { get; set; } = 10.0;
    public double AngleDeg { get; set; }
    public bool RotaryAxis { get; set; }

    public LinearPattern() { }

    public LinearPattern(int count, double spacingMm = 10.0, double angleDeg = 0.0, bool rotaryAxis = false)
    {
        Count = Math.Max(1, count);
        SpacingMm = Math.Max(0, spacingMm);
        AngleDeg = NormalizeDegrees(angleDeg);
        RotaryAxis = rotaryAxis;
    }

    internal static double NormalizeDegrees(double angle)
    {
        var a = angle % 360.0;
        if (a < 0) a += 360.0;
        return a;
    }
}

/// <summary>
/// Circular array copy parameters (ported from Swift CircularPattern / CircularArrayCopyParams).
/// Copies are rotations of the base path about (CenterX, CenterY), spread evenly from
/// StartAngleDeg to EndAngleDeg; the base path itself sits at position 0 (StartAngleDeg).
/// EndAngleDeg keeps the full-circle meaning: a positive multiple of 360° (e.g. 360) stays
/// 360° instead of wrapping to 0, so the sweep span is preserved for rotation math.
/// </summary>
public sealed class CircularPattern
{
    public int Count { get; set; } = 8;
    public double CenterX { get; set; }
    public double CenterY { get; set; }
    public double StartAngleDeg { get; set; }
    public double EndAngleDeg { get; set; } = 360.0;
    public double RadiusMm { get; set; } = 25.0;

    public CircularPattern() { }

    public CircularPattern(int count, double centerX = 0.0, double centerY = 0.0,
        double startAngleDeg = 0.0, double endAngleDeg = 360.0, double radiusMm = 25.0)
    {
        Count = Math.Max(1, count);
        CenterX = centerX;
        CenterY = centerY;
        StartAngleDeg = LinearPattern.NormalizeDegrees(startAngleDeg);
        EndAngleDeg = endAngleDeg % 360.0 == 0.0 && endAngleDeg > 0.0
            ? 360.0
            : LinearPattern.NormalizeDegrees(endAngleDeg);
        RadiusMm = Math.Max(0, radiusMm);
    }
}

/// <summary>
/// Rectangular (grid) array copy parameters: Columns × Rows copies with X/Y spacing.
/// The base path occupies cell (0,0); with <see cref="RotaryAxis"/> set, the spacings are
/// applied to the A (rotary) axis instead of X/Y.
/// </summary>
public sealed class GridPattern
{
    public int Columns { get; set; } = 2;
    public int Rows { get; set; } = 2;
    public double ColumnSpacingMm { get; set; } = 10.0;
    public double RowSpacingMm { get; set; } = 10.0;
    public bool RotaryAxis { get; set; }

    public GridPattern() { }

    public GridPattern(int columns, int rows, double columnSpacingMm = 10.0, double rowSpacingMm = 10.0, bool rotaryAxis = false)
    {
        Columns = columns;
        Rows = rows;
        ColumnSpacingMm = columnSpacingMm;
        RowSpacingMm = rowSpacingMm;
        RotaryAxis = rotaryAxis;
    }
}

/// <summary>
/// Result of an array copy (mirrors Swift ArrayCopyResult: arrayType, originalID, copiedIDs,
/// totalCount, success, errorMessage) extended with the replicated G-code lines — the port's
/// actual deliverable: the base path's motion lines replicated with X/Y or A offsets.
/// </summary>
public sealed class ArrayCopyResult
{
    public ArrayCopyType ArrayType { get; init; }
    public Guid OriginalId { get; init; }
    public List<Guid> CopiedIds { get; init; } = new();
    public int TotalCount { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public List<string> GcodeLines { get; init; } = new();
}

/// <summary>
/// Array copy engine (ported from Swift ArrayCopyAndMergeEngine + ArrayCopyToolpath semantics).
/// Faithful behaviour: the base toolpath's G-code motion lines (G0/G1/G2/G3 with coordinates)
/// are replicated once per copy position with X/Y offsets (linear/grid) or rotations about a
/// center (circular); A-axis offset mode is available for rotary machines. Non-motion lines
/// (preamble/postamble, comments, M-codes) are emitted once, not per copy. All numbers are
/// formatted with the invariant culture.
/// </summary>
public static class ArrayCopyEngine
{
    public static ArrayCopyResult ComputeLinear(IReadOnlyList<string> baseGcode, LinearPattern pattern, Guid originalId = default)
    {
        if (pattern.Count < 1)
        {
            return Fail(ArrayCopyType.Linear, originalId, "Count must be at least 1");
        }

        double angleRad = pattern.AngleDeg * Math.PI / 180.0;
        double cosA = Math.Cos(angleRad), sinA = Math.Sin(angleRad);

        var positions = new List<(double Dx, double Dy, double Da, double RotateDeg)>();
        for (int i = 0; i < pattern.Count; i++)
        {
            if (pattern.RotaryAxis)
            {
                positions.Add((0, 0, i * pattern.SpacingMm, 0));
            }
            else
            {
                positions.Add((i * pattern.SpacingMm * cosA, i * pattern.SpacingMm * sinA, 0, 0));
            }
        }

        return Build(baseGcode, positions, ArrayCopyType.Linear, originalId, pattern.Count);
    }

    public static ArrayCopyResult ComputeGrid(IReadOnlyList<string> baseGcode, GridPattern pattern, Guid originalId = default)
    {
        if (pattern.Columns < 1 || pattern.Rows < 1)
        {
            return Fail(ArrayCopyType.Grid, originalId, "Columns and rows must be at least 1");
        }

        var positions = new List<(double Dx, double Dy, double Da, double RotateDeg)>();
        for (int r = 0; r < pattern.Rows; r++)
        {
            for (int c = 0; c < pattern.Columns; c++)
            {
                if (pattern.RotaryAxis)
                {
                    positions.Add((0, 0, c * pattern.ColumnSpacingMm + r * pattern.RowSpacingMm, 0));
                }
                else
                {
                    positions.Add((c * pattern.ColumnSpacingMm, r * pattern.RowSpacingMm, 0, 0));
                }
            }
        }

        return Build(baseGcode, positions, ArrayCopyType.Grid, originalId, pattern.Columns * pattern.Rows);
    }

    public static ArrayCopyResult ComputeCircular(IReadOnlyList<string> baseGcode, CircularPattern pattern, Guid originalId = default)
    {
        if (pattern.Count < 1)
        {
            return Fail(ArrayCopyType.Circular, originalId, "Count must be at least 1");
        }

        if (pattern.RadiusMm <= 0)
        {
            return Fail(ArrayCopyType.Circular, originalId, "Radius must be positive");
        }

        double span = pattern.EndAngleDeg - pattern.StartAngleDeg;
        double step = pattern.Count > 1 ? span / (pattern.Count - 1) : 0.0;

        var positions = new List<(double Dx, double Dy, double Da, double RotateDeg)>();
        for (int i = 0; i < pattern.Count; i++)
        {
            // Each copy is ROTATED about (CenterX, CenterY) — convention:
            // the part's own distance from that centre is the array radius. RadiusMm is
            // validated for the rotary-axis case but is deliberately NOT a translation
            // here: adding one on top of the rotation would double-transform the part.
            // Stride is span/(Count-1), pinned by ArrayMergeRotaryTests (4 copies over
            // 360 degrees => 0/120/240/360).
            positions.Add((0, 0, 0, pattern.StartAngleDeg + i * step));
        }

        return Build(baseGcode, positions, ArrayCopyType.Circular, originalId, pattern.Count);
    }

    // ---------------------------------------------------------------- internals

    private static ArrayCopyResult Fail(ArrayCopyType type, Guid originalId, string message)
        => new()
        {
            ArrayType = type,
            OriginalId = originalId == default ? Guid.NewGuid() : originalId,
            TotalCount = 0,
            Success = false,
            ErrorMessage = message
        };

    private static ArrayCopyResult Build(
        IReadOnlyList<string> baseGcode,
        List<(double Dx, double Dy, double Da, double RotateDeg)> positions,
        ArrayCopyType type,
        Guid originalId,
        int totalCount)
    {
        var copiedIds = new List<Guid>();
        for (int i = 1; i < totalCount; i++) copiedIds.Add(Guid.NewGuid());

        int firstMotion = -1, lastMotion = -1;
        for (int i = 0; i < baseGcode.Count; i++)
        {
            if (GcodeMotion.IsMotionLine(baseGcode[i]))
            {
                if (firstMotion < 0) firstMotion = i;
                lastMotion = i;
            }
        }

        var g = new List<string>();
        if (firstMotion < 0)
        {
            g.AddRange(baseGcode);
            return new ArrayCopyResult
            {
                ArrayType = type,
                OriginalId = originalId == default ? Guid.NewGuid() : originalId,
                CopiedIds = copiedIds,
                TotalCount = totalCount,
                Success = true,
                GcodeLines = g
            };
        }

        // Preamble (everything before the first motion line) is emitted once.
        for (int i = 0; i < firstMotion; i++) g.Add(baseGcode[i]);
        // Motion lines replicated per position (position 0 is the original, zero offset).
        foreach (var (dx, dy, da, rot) in positions)
        {
            for (int i = firstMotion; i <= lastMotion; i++)
            {
                if (GcodeMotion.IsMotionLine(baseGcode[i]))
                {
                    g.Add(GcodeMotion.WithOffset(baseGcode[i], dx, dy, da, rot));
                }
            }
        }
        // Postamble (everything after the last motion line) is emitted once.
        for (int i = lastMotion + 1; i < baseGcode.Count; i++) g.Add(baseGcode[i]);

        return new ArrayCopyResult
        {
            ArrayType = type,
            OriginalId = originalId == default ? Guid.NewGuid() : originalId,
            CopiedIds = copiedIds,
            TotalCount = totalCount,
            Success = true,
            GcodeLines = g
        };
    }
}

/// <summary>
/// Shared G-code motion-line utilities (used by ArrayCopyEngine and MergedToolpathEngine).
/// A "motion line" is a G0/G1/G2/G3 command carrying at least one coordinate word.
/// </summary>
internal static class GcodeMotion
{
    public static bool IsMotionLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith('(') || trimmed.StartsWith(';')) return false;

        bool hasMotion = false, hasCoord = false;
        foreach (var tok in Split(trimmed))
        {
            if (tok.Length < 2) continue;
            char c = char.ToUpperInvariant(tok[0]);
            if (c == 'G')
            {
                if (double.TryParse(tok.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var gn) &&
                    (gn == 0 || gn == 1 || gn == 2 || gn == 3))
                {
                    hasMotion = true;
                }
            }
            else if (c is 'X' or 'Y' or 'Z' or 'A' or 'B' or 'C')
            {
                if (double.TryParse(tok.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    hasCoord = true;
                }
            }
        }
        return hasMotion && hasCoord;
    }

    /// <summary>Extracts the X/Y endpoint of a motion line, if both are present.</summary>
    public static bool TryGetPoint(string line, out VectorPoint point)
    {
        point = VectorPoint.Zero;
        if (!IsMotionLine(line)) return false;

        double? x = null, y = null;
        foreach (var tok in Split(line))
        {
            if (tok.Length < 2) continue;
            char c = char.ToUpperInvariant(tok[0]);
            if (c == 'X' && double.TryParse(tok.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var xv)) x = xv;
            else if (c == 'Y' && double.TryParse(tok.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var yv)) y = yv;
        }
        if (x is null || y is null) return false;
        point = new VectorPoint(x.Value, y.Value);
        return true;
    }

    /// <summary>
    /// Rebuilds a motion line with a transform applied to its coordinates:
    /// translation (Dx, Dy) on X/Y, rotation RotateDeg about (0,0), and Da on the A axis.
    /// In rotary mode the offsets apply to A instead of X/Y. Non-coordinate words are kept verbatim.
    /// </summary>
    public static string WithOffset(string line, double dx, double dy, double da, double rotateDeg = 0.0,
        bool rotary = false)
    {
        var tokens = Split(line);
        var result = new List<string>(tokens.Length);

        double? x = null, y = null;
        foreach (var tok in tokens)
        {
            if (tok.Length < 2) continue;
            char c = char.ToUpperInvariant(tok[0]);
            if (c == 'X' && double.TryParse(tok.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var xv)) x = xv;
            else if (c == 'Y' && double.TryParse(tok.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var yv)) y = yv;
        }

        double rot = rotateDeg * Math.PI / 180.0;
        double cosR = Math.Cos(rot), sinR = Math.Sin(rot);

        foreach (var tok in tokens)
        {
            if (tok.Length < 2) { result.Add(tok); continue; }
            char c = char.ToUpperInvariant(tok[0]);
            if (c == 'X' && double.TryParse(tok.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var xv))
            {
                double nx = rotary ? xv : xv + dx;
                if (!rotary && rotateDeg != 0.0 && x.HasValue && y.HasValue)
                {
                    nx = xv * cosR - y.Value * sinR + dx;
                }
                result.Add($"X{F3(nx)}");
            }
            else if (c == 'Y' && double.TryParse(tok.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var yv))
            {
                double ny = rotary ? yv : yv + dy;
                if (!rotary && rotateDeg != 0.0 && x.HasValue && y.HasValue)
                {
                    ny = x.Value * sinR + yv * cosR + dy;
                }
                result.Add($"Y{F3(ny)}");
            }
            else if (c == 'A' && double.TryParse(tok.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out var av))
            {
                result.Add($"A{F3(av + da)}");
            }
            else
            {
                result.Add(tok);
            }
        }

        return string.Join(" ", result);
    }

    public static string F3(double v)
        => (Math.Abs(v) < 1e-9 ? 0.0 : v).ToString("0.000", CultureInfo.InvariantCulture);

    private static string[] Split(string line)
        => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
}
