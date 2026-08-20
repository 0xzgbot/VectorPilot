using VectorPilot.Engine;
using VectorPilot.Geometry;

namespace VectorPilot.App;

/// <summary>
/// The single gate both Cut's Calculate and Machine's Start consult, so a job that Cut
/// refused cannot be streamed anyway through the Machine stage.
///
/// Previously the two disagreed: CutPanel checked whether an area strategy had a closed
/// outline, while MachinePanel.StreamStart_Click streamed AppState.LoadedGCode with no
/// validation at all. Loading a program from disk, or calculating before fixing the
/// geometry, put junk on the machine with nothing in the way.
/// </summary>
public static class JobGate
{
    /// <summary>Strategies that need an enclosed region to mean anything.</summary>
    private static readonly string[] AreaStrategies = { "profile", "pocket", "vcarve" };

    /// <summary>A shape with no inside: an open path that is not implicitly closed.</summary>
    public static bool IsOpen(VectorShape s)
        => !s.Closed && s.Type != ShapeType.Circle && s.Type != ShapeType.Rectangle;

    /// <summary>
    /// Why this selection cannot be cut with this strategy, or null if it can.
    /// </summary>
    public static string? AreaStrategyBlocker(
        string strategyKey, string displayName, IReadOnlyList<VectorShape> shapes)
    {
        if (!AreaStrategies.Contains(strategyKey)) return null;
        if (shapes.Count == 0) return null;

        int open = shapes.Count(IsOpen);
        if (open < shapes.Count) return null;   // at least one closed shape to cut

        return $"{displayName} needs a closed outline — {open} selected shape(s) are open paths. " +
               "Close them, or use Extend in Design to make the ends meet.";
    }

    /// <summary>
    /// Why this program must not be streamed, or null if it is safe to start.
    ///
    /// A program that is only comments is the dangerous case: it looks runnable, the machine
    /// accepts it, and the operator watches a job that never cuts.
    /// </summary>
    public static string? StreamBlocker(IReadOnlyList<string> gcode)
    {
        if (gcode.Count == 0) return "Nothing to stream — load or calculate a program first.";

        bool hasMotion = gcode.Any(l =>
        {
            var s = l.TrimStart();
            return s.StartsWith("G0") || s.StartsWith("G1")
                || s.StartsWith("G2") || s.StartsWith("G3");
        });

        if (!hasMotion)
            return "This program contains no cutting moves — only comments. " +
                   "Recalculate the toolpath before streaming.";

        return null;
    }

    /// <summary>
    /// Geometry problems worth surfacing before a cut: open contours and self-intersections.
    /// Returns an empty list when the geometry is clean.
    /// </summary>
    public static List<VectorDoctorIssue> Diagnose(IReadOnlyList<VectorShape> shapes)
        => shapes.Count == 0 ? new List<VectorDoctorIssue>() : VectorPreflightDoctor.Check(shapes);
}
