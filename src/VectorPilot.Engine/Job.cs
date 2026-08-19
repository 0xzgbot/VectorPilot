namespace VectorPilot.Engine;

/// <summary>The open document (mirrors ShopPilot Job).</summary>
public sealed class Job
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled";
    public string? FilePath { get; set; }
    public List<Sheet> Sheets { get; } = new();

    public Job()
    {
        Sheets.Add(new Sheet());
    }

    private Job(bool empty)
    {
        // no default sheet (used by CreateEmpty)
    }

    public static Job CreateEmpty() => new Job(true);

    public Sheet ActiveSheet => Sheets.FirstOrDefault() ?? Sheets[0];
    public bool IsDirty { get; set; }
    public bool IsDoubleSided { get; set; }
    public bool IsRotary { get; set; }
    public List<KeepOutZone> KeepOutZones { get; set; } = new();

    /// <summary>Which way the stock is turned over on a two-sided job.</summary>
    public FlipAxis FlipAxis { get; set; } = FlipAxis.Vertical;

    /// <summary>Datum holes for re-aligning the stock after the flip.</summary>
    public List<VectorPilot.Geometry.VectorPoint> RegistrationHoles { get; set; } = new();

    // SPK-1106a: precomputed V-Carve from sign recipe (carries full result so
    // the tree node can materialize in Cut/preview/machine handoff).
    public int VcarvePasses { get; set; }
    public double VcarveTimeSeconds { get; set; }
    public List<string>? VcarveGCode { get; set; }
    public string? VcarveParamsJSON { get; set; }

    public static Job CreateDefault() => new();
}
