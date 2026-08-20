using VectorPilot.Engine.IO;

namespace VectorPilot.Engine;

/// <summary>
/// Toolpath ⇄ PersistedToolpath mapper (the .shoppilot package stores the DTO
/// shape — Id/Name/Strategy/CutDepth/FeedRate/SpindleSpeed/IsDirty/GCode).
/// </summary>
public static class ToolpathPersistence
{
    public static PersistedToolpath ToPersisted(Toolpath t) => new()
    {
        Id = t.Id.ToString(),
        Name = t.Name,
        Strategy = t.Strategy.ToString(),
        CutDepth = t.CutDepth,
        FeedRate = t.FeedRate,
        SpindleSpeed = t.SpindleSpeed,
        IsDirty = t.IsDirty,
        GCode = t.GCode.ToList(),

        // Both of these were dropped: a saved job reloaded as the wrong strategy with
        // default params, which cut differently from what the user saved.
        StrategyKey = t.StrategyKey,
        ParamsJson = t.ParamsJson
    };

    public static Toolpath FromPersisted(PersistedToolpath p)
    {
        var t = new Toolpath
        {
            Name = p.Name,
            CutDepth = p.CutDepth,
            FeedRate = p.FeedRate,
            SpindleSpeed = p.SpindleSpeed,
            IsDirty = p.IsDirty
        };
        if (Guid.TryParse(p.Id, out var id)) t.SetId(id);
        if (Enum.TryParse<ToolpathStrategy>(p.Strategy, ignoreCase: true, out var strategy)) t.Strategy = strategy;

        // Restore the exact key and params. Older documents carry neither, so fall back to
        // the enum-derived key and leave params at their defaults.
        if (!string.IsNullOrWhiteSpace(p.StrategyKey)) t.StrategyKey = p.StrategyKey!;
        if (!string.IsNullOrWhiteSpace(p.ParamsJson)) t.ParamsJson = p.ParamsJson!;

        t.GCode.AddRange(p.GCode);
        return t;
    }
}
