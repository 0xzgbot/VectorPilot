using VectorPilot.Engine;

/// <summary>Preflight-checklist item (SPK-0412a): required items gate the run
/// and can never be bypassed.</summary>
public sealed class PreflightChecklistItem
{
    public string Id { get; init; } = "";
    public string Title { get; init; } = "";
    public bool Required { get; init; }
    public bool Acknowledged { get; private set; }

    public void Acknowledge() => Acknowledged = true;
    public void Unacknowledge() => Acknowledged = false;
}

/// <summary>
/// Preflight checklist (ported from the SPK-0412a contract): the machine-run
/// checklist REQUIRES spindle and work-zero items — the run is gated until
/// both are acknowledged, and required items can never be bypassed.
/// </summary>
public sealed class PreflightChecklist
{
    public List<PreflightChecklistItem> Items { get; } = new();

    public PreflightChecklistItem Spindle => Items.First(i => i.Id == "spindle");
    public PreflightChecklistItem WorkZero => Items.First(i => i.Id == "work-zero");

    public static PreflightChecklist CreateDefault() => new()
    {
        Items =
        {
            new PreflightChecklistItem { Id = "spindle", Title = "Spindle confirmed OFF before setup", Required = true },
            new PreflightChecklistItem { Id = "work-zero", Title = "Work zero verified at material corner", Required = true },
            new PreflightChecklistItem { Id = "material", Title = "Material secured to spoilboard", Required = true },
            new PreflightChecklistItem { Id = "dust", Title = "Dust collection connected", Required = false }
        }
    };

    /// <summary>The run is gated until every REQUIRED item is acknowledged.</summary>
    public bool IsComplete => Items.Where(i => i.Required).All(i => i.Acknowledged);

    /// <summary>Required items can never be bypassed: ClearAcknowledged resets
    /// only non-required items; a required item always stays required.</summary>
    public bool CanBypass(PreflightChecklistItem item) => !item.Required;

    public List<string> MissingRequired() => Items.Where(i => i.Required && !i.Acknowledged).Select(i => i.Title).ToList();
}
