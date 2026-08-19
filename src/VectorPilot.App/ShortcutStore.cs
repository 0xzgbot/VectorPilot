using System.Text.Json;

namespace VectorPilot.App;

/// <summary>
/// User-remappable keyboard shortcuts (Mac SPK-UXPOLISH parity). Commands are
/// identified by a stable id; the store maps id → gesture string ("Ctrl+G").
/// Defaults live in code, overrides persist as JSON, and a gesture can only be
/// bound to one command at a time.
/// </summary>
public sealed class ShortcutStore
{
    private static readonly Dictionary<string, string> Defaults = new()
    {
        ["undo"] = "Ctrl+Z",
        ["redo"] = "Ctrl+Y",
        ["duplicate"] = "Ctrl+D",
        ["select-all"] = "Ctrl+A",
        ["group"] = "Ctrl+G",
        ["ungroup"] = "Ctrl+Shift+G",
        ["delete"] = "Delete",
        ["palette"] = "Ctrl+K",
        ["save"] = "Ctrl+S",
        ["open"] = "Ctrl+O",
        ["fit-view"] = "Ctrl+0",
        ["node-mode"] = "N",
    };

    private readonly Dictionary<string, string> _overrides = new();

    /// <summary>Every command id the store knows about.</summary>
    public IReadOnlyCollection<string> CommandIds => Defaults.Keys;

    /// <summary>Effective gesture for a command (override if set, else default).</summary>
    public string? Gesture(string commandId)
        => _overrides.TryGetValue(commandId, out var g) ? g
         : Defaults.TryGetValue(commandId, out var d) ? d
         : null;

    public string? DefaultGesture(string commandId)
        => Defaults.TryGetValue(commandId, out var d) ? d : null;

    public bool IsRemapped(string commandId) => _overrides.ContainsKey(commandId);

    /// <summary>The command currently bound to a gesture, if any.</summary>
    public string? CommandFor(string gesture)
        => CommandIds.FirstOrDefault(id =>
               string.Equals(Gesture(id), gesture, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Bind a gesture to a command. Fails when the command is unknown or the
    /// gesture is already taken by a different command (no silent shadowing).
    /// </summary>
    public bool Remap(string commandId, string gesture)
    {
        if (!Defaults.ContainsKey(commandId)) return false;
        if (string.IsNullOrWhiteSpace(gesture)) return false;

        var holder = CommandFor(gesture);
        if (holder is not null && holder != commandId) return false;

        if (string.Equals(gesture, DefaultGesture(commandId), StringComparison.OrdinalIgnoreCase))
            _overrides.Remove(commandId);      // back to default: drop the override
        else
            _overrides[commandId] = gesture;

        return true;
    }

    /// <summary>Drop a single override.</summary>
    public bool ResetCommand(string commandId) => _overrides.Remove(commandId);

    /// <summary>Drop every override.</summary>
    public void ResetAll() => _overrides.Clear();

    public string ToJson() => JsonSerializer.Serialize(_overrides);

    public void LoadJson(string json)
    {
        _overrides.Clear();
        if (string.IsNullOrWhiteSpace(json)) return;

        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (map is null) return;
            foreach (var (id, gesture) in map)
                Remap(id, gesture);            // re-validated, so a bad file cannot create conflicts
        }
        catch (JsonException)
        {
            // A corrupt file falls back to defaults rather than throwing at startup.
        }
    }
}
