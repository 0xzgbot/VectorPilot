using VectorPilot.Engine;
using VectorPilot.Serial;

namespace VectorPilot.App;

/// <summary>
/// Post auto-select from the machine profile (SPK-0415): machine type picks the
/// post (grbl → GRBL, universal → Universal), units pick the modal (mm → G21,
/// in → G20), and the file extension follows the post.
/// </summary>
public static class PostSelector
{
    public static (PostProcessorType Post, GCodeUnits Units, string Extension) ForProfile(MachineProfile profile)
    {
        var post = profile.MachineType == MachineType.Universal ? PostProcessorType.Universal : PostProcessorType.Grbl;
        var units = profile.Units == MachineUnits.Inch ? GCodeUnits.Inch : GCodeUnits.Millimeter;
        var ext = profile.MachineType == MachineType.Universal ? "nc" : "gcode";
        return (post, units, ext);
    }
}
