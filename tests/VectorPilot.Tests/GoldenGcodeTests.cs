using System.IO;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Golden G-code gate: engine output with fixed params must byte-match the
/// committed golden files (the harness equivalent of the Mac's verify CLTs).
/// Regenerate intentionally with tests/goldens/regenerate-goldens.bat or by
/// deleting the golden and running with GENERATE_GOLDENS=1.
/// </summary>
public class GoldenGcodeTests
{
    /// <summary>tests/VectorPilot.Tests/goldens/ inside the repo (walk up from the bin dir).</summary>
    private static string GoldensDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "VectorPilot.sln")))
            {
                dir = dir.Parent;
            }
            return dir is null
                ? Path.Combine(AppContext.BaseDirectory, "goldens")
                : Path.Combine(dir.FullName, "tests", "VectorPilot.Tests", "goldens");
        }
    }

    private static string GoldenPath(string name) => Path.Combine(GoldensDir, name + ".gcode");

    [Fact]
    public void Profile_Engine_Matches_Golden()
    {
        var p = new ProfileToolpathParams
        {
            CutMode = ProfileCutMode.OnCut,
            MaxDepthOfCutMm = 2.0,
            FeedRateMmPerMin = 1000,
            ToolDiameterMm = 6.0,
            SpindleRpm = 12000
        };
        var r = ProfileToolpathEngine.Compute(new[] { VectorShape.Rectangle(0, 0, 20, 10) }, p, stockHeightMm: 4.0);
        AssertGolden("profile", r.GcodeLines);
    }

    [Fact]
    public void VCarve_Engine_Matches_Golden()
    {
        var v = new VCarveParams { VBitAngleDegrees = 90, MaxDepthOfCutMm = 2.0, FeedRateMmPerMin = 1000, SpindleRpm = 12000 };
        var r = VCarveEngine.Compute(new[] { VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(10, 0)) }, v);
        AssertGolden("vcarve", r.GcodeLines);
    }

    [Fact]
    public void Rough3D_Matches_Golden()
    {
        var heights = new double[64];
        Array.Fill(heights, 4.0);
        for (int i = 0; i < 8; i++) heights[4 * 8 + i] = 0; // trench
        var hf = new HeightfieldData(8, 8, 1.0, 0, 0, heights);
        var p = new HeightfieldRoughParams { StepDownMm = 2.0, StepOverMm = 1.0, StockAllowanceMm = 0.5, FeedRateMmPerMin = 1000 };
        var r = HeightfieldRoughEngine.Compute(hf, p);
        AssertGolden("rough3d", r.GcodeLines);
    }

    private static void AssertGolden(string name, List<string> lines)
    {
        string path = GoldenPath(name);
        string actual = string.Join('\n', lines) + "\n";

        if (Environment.GetEnvironmentVariable("GENERATE_GOLDENS") == "1" || !File.Exists(path))
        {
            Directory.CreateDirectory(GoldensDir);
            File.WriteAllText(path, actual);
            return;
        }

        string golden = File.ReadAllText(path);
        Assert.True(
            string.Equals(golden, actual, StringComparison.Ordinal),
            $"G-code drift for {name}:\n--- golden ---\n{golden}\n--- actual ---\n{actual}");
    }
}
