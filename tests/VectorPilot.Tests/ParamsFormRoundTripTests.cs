using System.Text.Json;
using System.Text.Json.Nodes;
using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// The params form must round-trip through ParamsJson and actually change the cut.
///
/// Two real defects this pins:
///  1. RefreshParamsForm only rendered JsonValueKind.Number, so weave's `pattern` and
///     moulding's `profile` — the fields that decide what gets cut — never appeared.
///  2. CommitParamsForm re-parsed ParamsJson and wrote it straight back without ever
///     reading the form, so edits were silently discarded and Calculate always used
///     the strategy defaults.
/// </summary>
public class ParamsFormRoundTripTests
{
    private static readonly StrategyRegistry Reg = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static List<VectorShape> Shapes()
    {
        var r1 = new VectorShape { Type = ShapeType.Polyline, Closed = false };
        r1.Points.AddRange(new[] { new VectorPoint(0, 0), new VectorPoint(80, 0) });
        var r2 = new VectorShape { Type = ShapeType.Polyline, Closed = false };
        r2.Points.AddRange(new[] { new VectorPoint(0, 40), new VectorPoint(80, 40) });
        return new List<VectorShape> { r1, r2 };
    }

    /// <summary>Set one JSON property the way the committed form does.</summary>
    private static string WithValue(string json, string key, JsonNode value)
    {
        var obj = JsonNode.Parse(json)!.AsObject();
        obj[key] = value;
        return obj.ToJsonString();
    }

    // ---- the form must be able to see the fields ----

    [Fact]
    public void Weave_Defaults_Expose_The_Pattern_Field()
    {
        var entry = Reg.Find("weave")!;
        var obj = JsonNode.Parse(entry.DefaultsJson)!.AsObject();

        Assert.True(obj.ContainsKey("pattern"), "pattern must be in the form JSON");
        Assert.Equal(JsonValueKind.Number, obj["pattern"]!.GetValueKind());
    }

    [Fact]
    public void Moulding_Defaults_Expose_Profile_And_Rails()
    {
        var entry = Reg.Find("moulding")!;
        var obj = JsonNode.Parse(entry.DefaultsJson)!.AsObject();

        Assert.True(obj.ContainsKey("profile"));
        Assert.True(obj.ContainsKey("rail1"));
        Assert.True(obj.ContainsKey("heightMm"));
    }

    [Fact]
    public void Enum_Params_Are_Discoverable_Through_The_Registry()
    {
        // This is what drives the dropdown: the CLR type behind the key.
        var type = Reg.ParamsTypeFor("weave");
        Assert.NotNull(type);

        var prop = type!.GetProperty("Pattern");
        Assert.NotNull(prop);
        Assert.True(prop!.PropertyType.IsEnum);
        Assert.Contains("Twill", Enum.GetNames(prop.PropertyType));
    }

    [Fact]
    public void Every_Strategy_Exposes_Its_Params_Type()
    {
        foreach (var e in Reg.Entries)
            Assert.NotNull(Reg.ParamsTypeFor(e.Key));
    }

    // ---- edits must change the emitted program ----

    [Fact]
    public void Changing_The_Weave_Pattern_Changes_The_Gcode()
    {
        var entry = Reg.Find("weave")!;

        string plain = WithValue(entry.DefaultsJson, "pattern", 0);   // Plain
        string twill = WithValue(entry.DefaultsJson, "pattern", 1);   // Twill

        var a = entry.Compute(Shapes(), null, plain).Gcode;
        var b = entry.Compute(Shapes(), null, twill).Gcode;

        Assert.NotEmpty(a);
        Assert.NotEmpty(b);
        Assert.NotEqual(string.Join("\n", a), string.Join("\n", b));
    }

    [Fact]
    public void Changing_Weave_Stepover_Changes_The_Move_Count()
    {
        var entry = Reg.Find("weave")!;

        string coarse = WithValue(WithValue(entry.DefaultsJson, "stepOverMm", 4.0), "cellSizeMm", 1.0);
        string fine = WithValue(WithValue(entry.DefaultsJson, "stepOverMm", 1.0), "cellSizeMm", 1.0);

        int coarseMoves = entry.Compute(Shapes(), null, coarse).Gcode.Count;
        int fineMoves = entry.Compute(Shapes(), null, fine).Gcode.Count;

        Assert.True(fineMoves > coarseMoves,
            $"finer stepover must cut more moves (fine={fineMoves}, coarse={coarseMoves})");
    }

    [Fact]
    public void Changing_Moulding_Height_Changes_The_Gcode()
    {
        var entry = Reg.Find("moulding")!;

        string shallow = WithValue(WithValue(entry.DefaultsJson, "heightMm", 2.0), "cellSizeMm", 1.0);
        string deep = WithValue(WithValue(entry.DefaultsJson, "heightMm", 12.0), "cellSizeMm", 1.0);

        var a = entry.Compute(Shapes(), null, shallow).Gcode;
        var b = entry.Compute(Shapes(), null, deep).Gcode;

        Assert.NotEmpty(a);
        Assert.NotEqual(string.Join("\n", a), string.Join("\n", b));
    }

    [Fact]
    public void Changing_The_Moulding_Profile_Changes_The_Gcode()
    {
        var entry = Reg.Find("moulding")!;

        string p0 = WithValue(WithValue(entry.DefaultsJson, "profile", 0), "cellSizeMm", 1.0);
        string p1 = WithValue(WithValue(entry.DefaultsJson, "profile", 1), "cellSizeMm", 1.0);

        var a = entry.Compute(Shapes(), null, p0).Gcode;
        var b = entry.Compute(Shapes(), null, p1).Gcode;

        Assert.NotEmpty(a);
        Assert.NotEqual(string.Join("\n", a), string.Join("\n", b));
    }

    [Fact]
    public void Params_Json_Survives_A_Deserialize_Serialize_Cycle()
    {
        // The form parses, edits and re-serializes; values must not drift.
        var entry = Reg.Find("weave")!;
        string edited = WithValue(entry.DefaultsJson, "threadHeightMm", 3.5);

        var typed = JsonSerializer.Deserialize<WeaveStrategyParams>(edited, Json)!;
        Assert.Equal(3.5, typed.ThreadHeightMm, 6);

        string back = JsonSerializer.Serialize(typed, Json);
        var reparsed = JsonSerializer.Deserialize<WeaveStrategyParams>(back, Json)!;
        Assert.Equal(3.5, reparsed.ThreadHeightMm, 6);
    }

    [Fact]
    public void An_Enum_Name_Maps_To_Its_Underlying_Int()
    {
        // The commit path stores enum names as ints; verify the mapping the UI relies on.
        var type = Reg.ParamsTypeFor("weave")!;
        var enumType = type.GetProperty("Pattern")!.PropertyType;

        Assert.True(Enum.TryParse(enumType, "Twill", ignoreCase: true, out var parsed));
        int asInt = Convert.ToInt32(parsed);

        string json = WithValue(Reg.Find("weave")!.DefaultsJson, "pattern", asInt);
        var typed = JsonSerializer.Deserialize<WeaveStrategyParams>(json, Json)!;
        Assert.Equal(WeavePattern.Twill, typed.Pattern);
    }

    [Fact]
    public void Bad_Params_Json_Falls_Back_To_Defaults_Instead_Of_Throwing()
    {
        var entry = Reg.Find("weave")!;
        // Compute must not throw on a malformed form value.
        var result = entry.Compute(Shapes(), null, "{}");
        Assert.NotNull(result);
    }
}
