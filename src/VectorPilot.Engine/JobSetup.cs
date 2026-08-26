namespace VectorPilot.Engine;

/// <summary>Modeling resolution (Standard ≈ 1M points, High = 4M).</summary>
public enum ModelingResolution { Standard, High }

/// <summary>
/// Job Setup options (docked Job Setup): sheet size,
/// material, datum (crosshair + offset), modeling resolution, units.
/// </summary>
public sealed class JobSetupOptions
{
    public double SheetWidthMm { get; set; } = 1220;
    public double SheetDepthMm { get; set; } = 610;
    public double MaterialThicknessMm { get; set; } = 12.7;
    public string MaterialName { get; set; } = "Softwood";
    public bool UseCrosshairDatum { get; set; } = true;
    public double DatumOffsetXMm { get; set; }
    public double DatumOffsetYMm { get; set; }
    public ModelingResolution Resolution { get; set; } = ModelingResolution.Standard;
    public string Units { get; set; } = "mm";

    /// <summary>Approximate model point budget (Standard = 1M, High = 4M).</summary>
    public int PointBudget => Resolution == ModelingResolution.High ? 4_000_000 : 1_000_000;

    /// <summary>Apply these options onto a sheet (size + material).</summary>
    public void ApplyTo(Sheet sheet, Material? material = null)
    {
        sheet.Width = SheetWidthMm;
        sheet.Height = SheetDepthMm;
        sheet.Thickness = MaterialThicknessMm;
        if (material is not null) sheet.Material = material;
    }

    /// <summary>Load from a sheet (round-trip support).</summary>
    public static JobSetupOptions From(Sheet sheet)
    {
        var o = new JobSetupOptions
        {
            SheetWidthMm = sheet.Width,
            SheetDepthMm = sheet.Height,
            MaterialThicknessMm = sheet.Thickness
        };
        if (sheet.Material is not null) o.MaterialName = sheet.Material.Name;
        return o;
    }
}
