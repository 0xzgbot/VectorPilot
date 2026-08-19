using VectorPilot.Engine;

namespace VectorPilot.App;

/// <summary>
/// Layer visibility chips (Mac SPK-UXPOLISH parity): solo/isolate a layer and
/// restore the previous visibility state. Solo hides every other layer; clicking
/// solo again — or soloing a different layer — restores what was visible before,
/// so isolating is never destructive.
/// </summary>
public sealed class LayerVisibilityModel
{
    private Dictionary<string, bool>? _preSolo;
    private string? _soloed;

    /// <summary>Name of the currently soloed layer, or null when none.</summary>
    public string? SoloedLayer => _soloed;

    public bool IsSoloed(string layerName) => _soloed == layerName;

    /// <summary>
    /// Toggle solo for a layer. Returns true when the layer is now soloed, false
    /// when solo was cleared and the previous state restored.
    /// </summary>
    public bool ToggleSolo(Sheet sheet, string layerName)
    {
        if (_soloed == layerName)
        {
            Restore(sheet);
            return false;
        }

        // Capture the pre-solo state only on the first solo, so switching between
        // layers still restores the user's original visibility.
        _preSolo ??= sheet.Layers.ToDictionary(l => l.Name, l => l.Visible);

        foreach (var l in sheet.Layers) l.Visible = l.Name == layerName;
        _soloed = layerName;
        return true;
    }

    /// <summary>Clear solo and restore the captured visibility state.</summary>
    public void Restore(Sheet sheet)
    {
        if (_preSolo is { } snapshot)
        {
            foreach (var l in sheet.Layers)
                if (snapshot.TryGetValue(l.Name, out bool wasVisible)) l.Visible = wasVisible;
        }
        _preSolo = null;
        _soloed = null;
    }

    /// <summary>
    /// Drop solo state without touching visibility — used when the document is
    /// replaced, so a stale snapshot cannot be applied to a different sheet.
    /// </summary>
    public void Reset()
    {
        _preSolo = null;
        _soloed = null;
    }
}
