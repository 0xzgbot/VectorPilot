namespace VectorPilot.Engine;

/// <summary>The open document (mirrors ShopPilot Job).</summary>
public sealed class Job
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; set; } = "Untitled";
    public string? FilePath { get; set; }
    public List<Sheet> Sheets { get; } = new();

    public Job() => Sheets.Add(new Sheet());

    public Sheet ActiveSheet => Sheets.FirstOrDefault() ?? Sheets[0];
    public bool IsDirty { get; set; }
    public bool IsDoubleSided { get; set; }
    public bool IsRotary { get; set; }

    public static Job CreateDefault() => new();
}
