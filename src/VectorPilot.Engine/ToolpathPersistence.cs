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
        GCode = t.GCode.ToList()
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
        t.GCode.AddRange(p.GCode);
        return t;
    }
}
