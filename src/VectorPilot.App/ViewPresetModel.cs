using VectorPilot.Geometry;

namespace VectorPilot.App;

/// <summary>
/// Named camera/zoom presets for the design canvas (Mac SPK-UXPOLISH parity).
/// A preset is a pan offset plus a zoom factor; "fit" is computed from the sheet
/// rather than stored, so it stays correct when the stock size changes.
/// </summary>
public sealed class ViewPresetModel
{
    public sealed class Preset
    {
        public required string Name { get; init; }
        public double Zoom { get; init; } = 1.0;
        public double PanX { get; init; }
        public double PanY { get; init; }
        /// <summary>When true, zoom/pan are recomputed from the sheet on apply.</summary>
        public bool FitToSheet { get; init; }
    }

    private readonly List<Preset> _presets = new();

    public ViewPresetModel()
    {
        // Built-ins mirror the Mac's view menu.
        _presets.Add(new Preset { Name = "Fit sheet", FitToSheet = true });
        _presets.Add(new Preset { Name = "Actual size (100%)", Zoom = 1.0 });
        _presets.Add(new Preset { Name = "Zoom 200%", Zoom = 2.0 });
        _presets.Add(new Preset { Name = "Zoom 50%", Zoom = 0.5 });
    }

    public IReadOnlyList<Preset> Presets => _presets;

    /// <summary>Built-ins cannot be removed; user presets can.</summary>
    public int BuiltInCount => 4;

    public Preset? Find(string name)
        => _presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Capture the current view under a name, replacing any user preset with that name.</summary>
    public Preset Save(string name, double zoom, double panX, double panY)
    {
        var existing = Find(name);
        if (existing is not null && _presets.IndexOf(existing) >= BuiltInCount)
            _presets.Remove(existing);

        var preset = new Preset { Name = name, Zoom = zoom, PanX = panX, PanY = panY };
        _presets.Add(preset);
        return preset;
    }

    /// <summary>Remove a user preset. Built-ins are protected.</summary>
    public bool Remove(string name)
    {
        var p = Find(name);
        if (p is null) return false;
        if (_presets.IndexOf(p) < BuiltInCount) return false;
        return _presets.Remove(p);
    }

    /// <summary>
    /// Resolve a preset to a concrete zoom for the given viewport and sheet.
    /// Fit presets scale the sheet to fill the viewport with a small margin.
    /// </summary>
    public static double ResolveZoom(Preset preset, double viewportWidth, double viewportHeight,
                                     double sheetWidth, double sheetHeight)
    {
        if (!preset.FitToSheet) return preset.Zoom;
        if (sheetWidth <= 0 || sheetHeight <= 0) return 1.0;
        if (viewportWidth <= 0 || viewportHeight <= 0) return 1.0;

        double zx = viewportWidth / sheetWidth;
        double zy = viewportHeight / sheetHeight;
        return Math.Min(zx, zy) * 0.92;   // 8% margin
    }
}
