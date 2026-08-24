using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// P-202: V-carve flat-area clearing. A dumbbell / wide slot has regions whose
/// half-width exceeds what the V-bit can widen to by MaxDepthOfCutMm — there the
/// bit bottoms out and stock remains beside the spine. With FlatAreaClearing on,
/// those regions get extra G1 at max depth; narrow necks stay V-depth.
/// </summary>
public class VCarveFlatClearTests
{
    private static VCarveParams Params(bool flatClearing) => new()
    {
        VBitAngleDegrees = 90,        // tip width = 2·|z|: at 2mm deep, 4mm wide
        MaxDepthOfCutMm = 2,
        StepOverMm = 1,
        MedialAxisPass = true,
        MedialAxisCellMm = 0.5,
        FlatAreaClearing = flatClearing,
    };

    /// <summary>A wide slot: a 60×20mm closed rectangle. Half-width 10mm far exceeds
    /// the 2mm-reachable half-width (2mm at 90°) → a long flat run.</summary>
    private static VectorShape WideSlot()
        => new()
        {
            Type = ShapeType.Rectangle,
            Closed = true,
            Points = { new(0, 0), new(60, 0), new(60, 20), new(0, 20) }
        };

    [Fact]
    public void Flat_Clearing_Adds_Extra_G1_At_Max_Depth_In_Wide_Regions()
    {
        var vectors = new List<VectorShape> { WideSlot() };
        var without = VCarveEngine.Compute(vectors, Params(false));
        var with = VCarveEngine.Compute(vectors, Params(true));

        int deepWithout = without.GcodeLines.Count(l => l.Contains("Z-2.000"));
        int deepWith = with.GcodeLines.Count(l => l.Contains("Z-2.000"));
        Assert.True(deepWith > deepWithout,
            $"flat clearing added no full-depth moves ({deepWith} vs {deepWithout})");

        // And it announces itself.
        Assert.Contains(with.GcodeLines, l => l.Contains("Flat area clearing"));
        Assert.DoesNotContain(without.GcodeLines, l => l.Contains("Flat area clearing"));
    }

    [Fact]
    public void Narrow_Neck_Keeps_V_Depth_And_Is_Not_Swept_Flat()
    {
        // A thin 6×3 rectangle: at 90°, a 3mm-wide channel reaches only 1.5mm deep;
        // its half-width (1.5) is BELOW the threshold, so no flat sweep should fire.
        var neck = new VectorShape
        {
            Type = ShapeType.Rectangle,
            Closed = true,
            Points = { new(0, 0), new(6, 0), new(6, 3), new(0, 3) }
        };

        var result = VCarveEngine.Compute(new List<VectorShape> { neck }, Params(true));

        Assert.DoesNotContain(result.GcodeLines, l => l.Contains("Flat area clearing"));
    }

    [Fact]
    public void Flag_Off_By_Default_And_Round_Trips_Through_Params_Json()
    {
        // Off by default — existing programs are untouched.
        Assert.False(new VCarveParams().FlatAreaClearing);

        // The registry's vcarve defaults JSON must round-trip the flag so the Cut
        // params form can show/edit it.
        var entry = new StrategyRegistry().Find("vcarve");
        Assert.NotNull(entry);

        var p = System.Text.Json.JsonSerializer.Deserialize<VCarveParams>(
            entry!.DefaultsJson,
            new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });
        Assert.NotNull(p);
        Assert.False(p!.FlatAreaClearing);
    }
}
