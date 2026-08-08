namespace VectorPilot.Engine;

/// <summary>Combine mode for a relief component (ported from CombineModes.swift OperationMode).</summary>
public enum OperationMode
{
    CombineAdd, CombineSubtract, CombineMerge, CombineLow, CombineMax, CombineMin, CombineMultiply
}

/// <summary>
/// A 3D relief component: one heightfield plus its combine mode in the
/// component stack (ported from ReliefComponent.swift, SPK-0700 lean slice).
/// Dynamic props (scale/tilt/fade) apply at composite time; the stored grid
/// stays pristine.
/// </summary>
public sealed class ReliefComponent
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; set; } = "Relief";
    public HeightfieldData Heightfield { get; set; }
    public OperationMode CombineMode { get; set; } = OperationMode.CombineAdd;
    public bool Visible { get; set; } = true;
    public double? HeightScale { get; set; }
    public double? TiltAngleDegrees { get; set; }
    public double? FadeAmount { get; set; }
    public FadeDirection? FadeDirection { get; set; }

    public ReliefComponent(HeightfieldData heightfield) => Heightfield = heightfield;

    /// <summary>The heightfield with this component's dynamic props applied (scale → tilt → fade).</summary>
    public HeightfieldData ModifiedHeightfield
        => ComponentModifierEngine.Apply(Heightfield, HeightScale, TiltAngleDegrees, FadeAmount, FadeDirection);
}

/// <summary>
/// Element-wise combine engine over aligned heightfields (ported from
/// ComponentCompositor.swift, SPK-0701) — the real math behind Add/Subtract/
/// Merge-high/Low/Max/Min/Multiply.
/// </summary>
public static class ComponentCompositor
{
    /// <summary>Compose the visible components into the active relief, in list order.
    /// Returns null when no component is visible or the grids are not aligned.</summary>
    public static HeightfieldData? Composite(IReadOnlyList<ReliefComponent> components)
    {
        HeightfieldData? accumulator = null;
        foreach (var component in components.Where(c => c.Visible))
        {
            var grid = component.ModifiedHeightfield;
            if (accumulator is null)
            {
                accumulator = grid;
                continue;
            }
            var merged = Combine(accumulator, grid, component.CombineMode);
            if (merged is null) return null;
            accumulator = merged;
        }
        return accumulator;
    }

    /// <summary>Combine two aligned heightfields element-wise. Null when not aligned.</summary>
    public static HeightfieldData? Combine(HeightfieldData a, HeightfieldData b, OperationMode mode)
    {
        if (a.Width != b.Width || a.Height != b.Height ||
            Math.Abs(a.CellSizeMm - b.CellSizeMm) > 1e-9 ||
            Math.Abs(a.MinX - b.MinX) > 1e-9 || Math.Abs(a.MinY - b.MinY) > 1e-9)
        {
            return null;
        }
        double maxH = Math.Max(a.MaxHeight, b.MaxHeight);
        var heights = new double[a.Width * a.Height];
        for (int i = 0; i < a.Heights.Length; i++)
        {
            double ha = a.Heights[i], hb = b.Heights[i];
            double h = mode switch
            {
                OperationMode.CombineAdd => Math.Min(maxH, ha + hb),
                OperationMode.CombineSubtract => Math.Max(0, ha - hb),
                OperationMode.CombineMerge => Math.Max(ha, hb),
                OperationMode.CombineLow => Math.Min(ha, hb),
                OperationMode.CombineMax => Math.Max(ha, hb),
                OperationMode.CombineMin => Math.Min(ha, hb),
                OperationMode.CombineMultiply => maxH > 1e-9 ? Math.Min(maxH, ha * hb / maxH) : 0,
                _ => 0
            };
            heights[i] = Math.Max(0, h);
        }
        return new HeightfieldData(a.Width, a.Height, a.CellSizeMm, a.MinX, a.MinY, heights);
    }
}
