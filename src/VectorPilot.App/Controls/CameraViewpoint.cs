namespace VectorPilot.App.Controls;

/// <summary>
/// Named camera viewpoints for the 3D preview (animated camera). Kept in its own
/// file so tests can reference it without constructing a WPF control.
/// </summary>
public enum CameraViewpoint
{
    Isometric,
    Top,
    Front,
    Right
}
