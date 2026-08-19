using VectorPilot.Engine;

namespace VectorPilot.App;

/// <summary>
/// Maps <see cref="StrategyRegistry"/> keys to <see cref="ToolpathStrategy"/> and
/// back.
///
/// Why this exists: CutPanel used <c>strategy.ToString().ToLowerInvariant()</c> as
/// the registry key. That silently failed for every key that is not a lowercased
/// enum name — <c>photo-vcarve</c>, <c>bevel</c>, <c>inlay-pocket</c>, <c>dragknife</c>
/// and friends — so <c>Registry.Find</c> returned null and Calculate fell through to
/// the legacy profile-only generator. The user picked Photo V-Carve and got a
/// profile cut with no warning.
///
/// Keys that have no distinct enum case map onto the closest case; the toolpath's
/// StrategyKey carries the exact registry key so recalculation stays faithful.
/// </summary>
public static class StrategyKeyMap
{
    private static readonly Dictionary<string, ToolpathStrategy> KeyToEnum = new(StringComparer.OrdinalIgnoreCase)
    {
        ["profile"] = ToolpathStrategy.Profile,
        ["pocket"] = ToolpathStrategy.Pocket,
        ["vcarve"] = ToolpathStrategy.VCarve,
        ["drill"] = ToolpathStrategy.Drill,
        ["quickengrave"] = ToolpathStrategy.QuickEngrave,
        ["quickengrave2"] = ToolpathStrategy.QuickEngrave,
        ["prism"] = ToolpathStrategy.BevelCarving,
        ["fluting"] = ToolpathStrategy.Fluting,
        ["chamfer"] = ToolpathStrategy.Chamfer,
        ["bevel"] = ToolpathStrategy.BevelCarving,
        ["dragknife"] = ToolpathStrategy.Profile,
        ["texture"] = ToolpathStrategy.Texture,
        ["inlay-pocket"] = ToolpathStrategy.Inlay,
        ["inlay-plug"] = ToolpathStrategy.Inlay,
        ["laser-cut"] = ToolpathStrategy.Profile,
        ["laser-fill"] = ToolpathStrategy.Pocket,
        ["moulding"] = ToolpathStrategy.Moulding,
        ["weave"] = ToolpathStrategy.Weave,
        ["rough3d"] = ToolpathStrategy.Rough3D,
        ["finish3d"] = ToolpathStrategy.Finish3D,
        ["photo-vcarve"] = ToolpathStrategy.PhotoVCarve,
        ["sketch-carve"] = ToolpathStrategy.VCarve,
        ["rotary-wrap"] = ToolpathStrategy.RotaryWrap
    };

    /// <summary>Enum case for a registry key, or Profile when unknown.</summary>
    public static ToolpathStrategy ToStrategy(string key)
        => KeyToEnum.TryGetValue(key, out var s) ? s : ToolpathStrategy.Profile;

    /// <summary>
    /// Preferred registry key for an enum case. Used when loading an older document
    /// that stored only the enum; the exact key is preserved on new toolpaths.
    /// </summary>
    public static string ToKey(ToolpathStrategy strategy) => strategy switch
    {
        ToolpathStrategy.VCarve => "vcarve",
        ToolpathStrategy.Rough3D => "rough3d",
        ToolpathStrategy.Finish3D => "finish3d",
        ToolpathStrategy.PhotoVCarve => "photo-vcarve",
        ToolpathStrategy.RotaryWrap => "rotary-wrap",
        ToolpathStrategy.BevelCarving => "bevel",
        ToolpathStrategy.Inlay => "inlay-pocket",
        ToolpathStrategy.QuickEngrave => "quickengrave",
        _ => strategy.ToString().ToLowerInvariant()
    };

    /// <summary>Every key the map knows.</summary>
    public static IReadOnlyCollection<string> Keys => KeyToEnum.Keys;
}
