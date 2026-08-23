using VectorPilot.Engine;
using VectorPilot.Serial;

namespace VectorPilot.App;

/// <summary>Shared application state (single-window slice).</summary>
public static class AppState
{
    public static Job CurrentJob { get; private set; } = Job.CreateDefault();

    /// <summary>
    /// Beginner or Advanced operation set. Beginner trims the 28-strategy registry to eight
    /// approachable operations; Advanced offers everything. Read by CutPanel when it fills
    /// the strategy combo.
    /// </summary>
    public static UiMode UiMode { get; set; } = UiMode.Beginner;

    /// <summary>Replace the current job (autosave recovery path).</summary>
    public static void RestoreJob(Job job)
    {
        CurrentJob = job ?? Job.CreateDefault();
    }
    /// <summary>Heightfield from the latest 3D import (drives 3D strategies).</summary>
    public static HeightfieldData? Heightfield { get; set; }
    public static ToolpathTree Toolpaths { get; } = new();

    public static MachineProfile Profile { get; set; } = MachineProfile.Simulator();
    public static IMachineTransport Transport { get; private set; } = new SimulatorTransport();
    public static GCodeStreamer? Streamer { get; set; }
    public static List<string> LoadedGCode { get; set; } = new();
    public static string? LoadedGCodePath { get; set; }

    /// <summary>Composite relief baked from the Model stage, for 3D toolpathing.</summary>
    public static HeightfieldData? ModelHeightfield { get; set; }

    /// <summary>
    /// H-211: the shared relief component stack. The Model stage's tree owns the UI,
    /// but a grayscale photo relief lands here too so both stages see one stack.
    /// </summary>
    public static ComponentTreeViewModel Components { get; } = new();

    /// <summary>
    /// Card P2: shape ids belonging to the toolpath currently selected in the
    /// Toolpaths stage. The Design canvas highlights these so the link between a
    /// toolpath and the geometry it cuts is visible.
    /// </summary>
    public static HashSet<Guid> FollowedSourceShapeIds { get; } = new();

    /// <summary>Raised when <see cref="FollowedSourceShapeIds"/> changes.</summary>
    public static event Action? FollowedSourceChanged;

    public static void RaiseFollowedSourceChanged() => FollowedSourceChanged?.Invoke();

    public static void NewJob(double width, double height, double thickness, UnitSystem units, string materialName)
    {
        CurrentJob = Job.CreateDefault();
        var sheet = CurrentJob.ActiveSheet;
        sheet.Width = width;
        sheet.Height = height;
        sheet.Thickness = thickness;
        sheet.Units = units;
        sheet.Material = new Material { Name = materialName };
        CurrentJob.Name = "New Job";
        Toolpaths.Toolpaths.Clear();
        LoadedGCode.Clear();
        LoadedGCodePath = null;
    }

    public static void ReplaceTransport(IMachineTransport transport) => Transport = transport;

    /// <summary>Create a streamer bound to the current transport if none exists.</summary>
    public static GCodeStreamer EnsureStreamer()
    {
        if (Streamer is null || !ReferenceEquals(Streamer.TransportBinding, Transport))
        {
            Streamer = new GCodeStreamer(Transport);
        }
        return Streamer;
    }
}
