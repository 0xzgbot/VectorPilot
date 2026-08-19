using System.Text.Json;
using System.Text.Json.Serialization;
using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// E2: moulding and weave must emit cutting G-code through StrategyRegistry.Compute,
/// the same path CutPanel uses — not via a fake Title/Run API.
/// </summary>
public class MouldingWeaveStrategyTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static StrategyRegistry.Entry MustFind(string key)
    {
        var entry = new StrategyRegistry().Find(key);
        Assert.NotNull(entry);
        return entry!;
    }

    private static StrategyResult Run(string key, IReadOnlyList<VectorShape> shapes, object? parameters = null)
    {
        var entry = MustFind(key);
        string json = parameters is null
            ? entry.DefaultsJson
            : JsonSerializer.Serialize(parameters, parameters.GetType(), Json);
        return entry.Compute(shapes, null, json);
    }

    private static List<VectorShape> TwoRails()
    {
        var r1 = new VectorShape { Type = ShapeType.Polyline, Closed = false };
        r1.Points.AddRange(new[] { new VectorPoint(0, 0), new VectorPoint(100, 0) });

        var r2 = new VectorShape { Type = ShapeType.Polyline, Closed = false };
        r2.Points.AddRange(new[] { new VectorPoint(0, 40), new VectorPoint(100, 40) });

        return new List<VectorShape> { r1, r2 };
    }

    private static bool HasCuttingMove(IEnumerable<string> gcode) =>
        gcode.Any(l => l.StartsWith("G1", StringComparison.Ordinal) || l.StartsWith("G01", StringComparison.Ordinal));

    [Fact]
    public void Both_Strategies_Are_Registered()
    {
        var reg = new StrategyRegistry();
        Assert.NotNull(reg.Find("moulding"));
        Assert.NotNull(reg.Find("weave"));
    }

    [Fact]
    public void They_Have_Human_Readable_Names()
    {
        Assert.Equal("Moulding", MustFind("moulding").DisplayName);
        Assert.Equal("Weave", MustFind("weave").DisplayName);
    }

    [Fact]
    public void The_Strategy_Enum_Covers_Them()
    {
        Assert.True(Enum.IsDefined(typeof(ToolpathStrategy), ToolpathStrategy.Moulding));
        Assert.True(Enum.IsDefined(typeof(ToolpathStrategy), ToolpathStrategy.Weave));
    }

    [Fact]
    public void Moulding_Emits_Cutting_Moves_From_Two_Rails()
    {
        var result = Run("moulding", TwoRails(), new MouldingToolpathParams
        {
            HeightMm = 6, StepOverMm = 1.5, CellSizeMm = 1.0, Samples = 20
        });

        Assert.True(result.Gcode.Count >= 5, $"moulding produced {result.Gcode.Count} lines");
        Assert.True(HasCuttingMove(result.Gcode), "moulding must emit G1 through the registry");
    }

    [Fact]
    public void Moulding_Accepts_A_Single_Rail()
    {
        var result = Run("moulding", TwoRails().Take(1).ToList(), new MouldingToolpathParams
        {
            HeightMm = 6, StepOverMm = 1.5, CellSizeMm = 1.0, Samples = 20
        });

        Assert.NotEmpty(result.Gcode);
        Assert.True(HasCuttingMove(result.Gcode));
    }

    [Fact]
    public void Moulding_With_No_Geometry_Produces_Nothing()
    {
        var result = Run("moulding", new List<VectorShape>(), new MouldingToolpathParams());
        Assert.Empty(result.Gcode);
        Assert.False(HasCuttingMove(result.Gcode));
    }

    [Fact]
    public void Weave_Emits_Cutting_Moves()
    {
        var result = Run("weave", TwoRails(), new WeaveStrategyParams
        {
            Pattern = WeavePattern.Plain, CellSizeMm = 1.0, StepOverMm = 1.5
        });

        Assert.True(result.Gcode.Count >= 5, $"weave produced {result.Gcode.Count} lines");
        Assert.True(HasCuttingMove(result.Gcode), "weave must emit G1 through the registry");
    }

    [Fact]
    public void Weave_Falls_Back_To_Its_Own_Area_Without_Geometry()
    {
        var result = Run("weave", new List<VectorShape>(), new WeaveStrategyParams
        {
            WidthMm = 60, HeightMm = 60, CellSizeMm = 1.0, StepOverMm = 1.5
        });

        Assert.NotEmpty(result.Gcode);
        Assert.True(HasCuttingMove(result.Gcode));
    }

    [Fact]
    public void Weave_Patterns_Produce_Different_Programs()
    {
        List<string> Gcode(WeavePattern pattern) => Run("weave", new List<VectorShape>(), new WeaveStrategyParams
        {
            Pattern = pattern, WidthMm = 60, HeightMm = 60,
            CellSizeMm = 1.0, StepOverMm = 1.5
        }).Gcode;

        var plain = Gcode(WeavePattern.Plain);
        var twill = Gcode(WeavePattern.Twill);

        Assert.True(HasCuttingMove(plain));
        Assert.True(HasCuttingMove(twill));
        Assert.NotEqual(string.Join("\n", plain), string.Join("\n", twill));
    }

    [Fact]
    public void Weave_Cuts_Below_The_Surface()
    {
        var lines = Run("weave", new List<VectorShape>(), new WeaveStrategyParams
        {
            WidthMm = 60, HeightMm = 60, CellSizeMm = 1.0, StepOverMm = 1.5, ThreadHeightMm = 3.0
        }).Gcode;

        bool anyNegativeZ = lines.Any(l =>
            l.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(t => t.StartsWith("Z", StringComparison.Ordinal)
                    && double.TryParse(t[1..],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double z)
                    && z < 0));

        Assert.True(anyNegativeZ, "weave must actually cut into the stock");
    }
}
