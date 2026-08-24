using System.Text.RegularExpressions;
using VectorPilot.App;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// H-304: the inverse-mill flag on 3D Rough machines the CAVITY the model would
/// leave — the Z-complement of the relief. On a step field (left half tall, right
/// half zero) normal roughing can only ever clear the RIGHT half; inverse milling
/// must clear the LEFT half instead, and the emitted program says so.
/// </summary>
public class InverseMillTests
{
    private static readonly System.Text.Json.JsonSerializerOptions Camel =
        new() { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
    /// <summary>40×4mm grid, 1mm cells: columns 0–19 stand at 6mm, 20–39 at zero.</summary>
    private static HeightfieldData StepField()
    {
        int w = 40, h = 4;
        var heights = new double[w * h];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++)
                heights[j * w + i] = i < 20 ? 6.0 : 0.0;
        return new HeightfieldData(w, h, 1.0, 0, 0, heights);
    }

    private static HeightfieldRoughParams Params(bool inverse) => new()
    {
        ToolDiameterMm = 3,
        StepDownMm = 2,
        StepOverMm = 1,
        FeedRateMmPerMin = 1000,
        PlungeFeedRateMmPerMin = 300,
        SpindleRpm = 18000,
        InverseMill = inverse
    };

    /// <summary>X positions of every horizontal cutting move (G1 … X… on a Z'd line).</summary>
    private static List<double> CutXPositions(IReadOnlyList<string> gcode)
    {
        var xs = new List<double>();
        foreach (var line in gcode)
        {
            var m = Regex.Match(line, @"^G1 X(-?\d+(\.\d+)?) Y");
            if (m.Success) xs.Add(double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        }
        return xs;
    }

    [Fact]
    public void Inverse_Mill_Clears_The_Opposite_Half_Of_A_Step_Field()
    {
        var hf = StepField();

        var normal = HeightfieldRoughEngine.Compute(hf, Params(inverse: false));
        var inverse = HeightfieldRoughEngine.Compute(hf, Params(inverse: true));

        var normalX = CutXPositions(normal.GcodeLines);
        var inverseX = CutXPositions(inverse.GcodeLines);

        Assert.NotEmpty(normalX);
        Assert.NotEmpty(inverseX);

        // Normal roughing machines where the model ISN'T TALL — the right half.
        // Inverse milling flips it: only the LEFT (formerly solid) half is cut.
        Assert.All(normalX, x => Assert.True(x >= 19.9, $"normal cut at X={x} invaded the tall half"));
        Assert.All(inverseX, x => Assert.True(x <= 20.1, $"inverse cut at X={x} escaped the cavity half"));
    }

    [Fact]
    public void Inverse_Program_Differs_And_Declares_Itself()
    {
        var hf = StepField();

        var normal = HeightfieldRoughEngine.Compute(hf, Params(inverse: false));
        var inverse = HeightfieldRoughEngine.Compute(hf, Params(inverse: true));

        Assert.NotEqual(normal.GcodeLines, inverse.GcodeLines);
        Assert.DoesNotContain(normal.GcodeLines, l => l.Contains("Inverse mill"));
        Assert.Contains(inverse.GcodeLines, l => l.Contains("Inverse mill"));

        // The flag defaults OFF — existing programs are untouched.
        Assert.False(new HeightfieldRoughParams().InverseMill);
    }

    [Fact]
    public void The_Rough3d_Param_Row_Is_Ui_Reachable_In_The_Defaults_Json()
    {
        // The Cuts-list params form renders one editable row per numeric/bool key in
        // DefaultsJson (bool rows commit through bool.TryParse). If the key is in the
        // blob, the user can flip it before Calculate — that IS the checkbox.
        var entry = new StrategyRegistry().Find("rough3d");
        Assert.NotNull(entry);
        Assert.Contains("inverseMill", entry!.DefaultsJson, StringComparison.OrdinalIgnoreCase);

        // And the strategy round-trips it: defaults parse back with the flag off,
        // flipping it on survives serialization.
        var p = System.Text.Json.JsonSerializer.Deserialize<HeightfieldRoughParams>(
            entry.DefaultsJson, Camel)!;
        Assert.False(p.InverseMill);
        p.InverseMill = true;
        var json = System.Text.Json.JsonSerializer.Serialize(p, Camel);
        Assert.Contains("true", json);
    }
}
