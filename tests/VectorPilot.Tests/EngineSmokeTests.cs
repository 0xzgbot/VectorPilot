using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Stub guard: every ported engine must produce real output for a minimal
/// fixture. Prevents the "write-first stub survived the build" failure mode —
/// an empty engine class now fails here instead of passing silently.
/// </summary>
public class EngineSmokeTests
{
    private static VectorShape Square(double s = 10) => VectorShape.Rectangle(0, 0, s, s);
    private static HeightfieldData Ridge()
    {
        var h = new double[64];
        for (int j = 0; j < 8; j++)
            for (int i = 0; i < 8; i++)
                h[j * 8 + i] = i < 4 ? 2 : 6;
        return new HeightfieldData(8, 8, 1.0, 0, 0, h);
    }

    public static IEnumerable<object[]> Engines()
    {
        yield return new object[] { "Profile", new Func<List<string>>(() => ProfileToolpathEngine.Compute(new[] { Square() }, new ProfileToolpathParams()).GcodeLines) };
        yield return new object[] { "Pocket", new Func<List<string>>(() => PocketEngine.Generate(new[] { Square() }, 2, 2, 40, 1000, 300, 12000, 5).ToList()) };
        yield return new object[] { "VCarve", new Func<List<string>>(() => VCarveEngine.Compute(new[] { VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(10, 0)) }, new VCarveParams()).GcodeLines) };
        yield return new object[] { "Drill", new Func<List<string>>(() => DrillEngine.Compute(new[] { new DrillPoint(5, 5, 3.0) }, new DrillParams()).GcodeLines) };
        yield return new object[] { "QuickEngrave", new Func<List<string>>(() => QuickEngraveEngine.Compute(new[] { Square() }, new QuickEngraveParams()).GcodeLines) };
        yield return new object[] { "QuickEngraveToolpath", new Func<List<string>>(() => QuickEngraveToolpathEngine.Compute(new[] { Square() }, new QuickEngraveToolpathParams()).GcodeLines) };
        yield return new object[] { "PhotoVCarve", new Func<List<string>>(() => PhotoVCarveEngine.Compute(Ridge(), new PhotoVCarveToolpathParams()).GcodeLines) };
        yield return new object[] { "SketchCarve", new Func<List<string>>(() => SketchCarveEngine.Compute(Ridge(), new SketchCarveToolpathParams()).GcodeLines) };
        yield return new object[] { "Prism", new Func<List<string>>(() => PrismToolpathEngine.Compute(new[] { Square() }, new PrismToolpathParams()).GcodeLines) };
        yield return new object[] { "Fluting", new Func<List<string>>(() => FlutingToolpathEngine.Compute(new[] { Square() }, new FlutingToolpathParams()).GcodeLines) };
        yield return new object[] { "Chamfer", new Func<List<string>>(() => ChamferToolpathEngine.Compute(new[] { Square() }, new ChamferToolpathParams()).GcodeLines) };
        yield return new object[] { "BevelCarving", new Func<List<string>>(() => BevelCarvingEngine.Compute(new[] { Square() }, new BevelCarvingParams()).GcodeLines) };
        yield return new object[] { "DragKnife", new Func<List<string>>(() => DragKnifeToolpathEngine.Compute(new[] { Square() }, new DragKnifeToolpathParams()).GcodeLines) };
        yield return new object[] { "Texture", new Func<List<string>>(() => TextureToolpathEngine.Compute(new[] { Square() }, new TextureToolpathParams()).GcodeLines) };
        yield return new object[] { "InlayPocket", new Func<List<string>>(() => InlayToolpathEngine.ComputePocket(new[] { Square() }, new InlayToolpathParams()).GcodeLines) };
        yield return new object[] { "InlayPlug", new Func<List<string>>(() => InlayToolpathEngine.ComputePlug(new[] { Square() }, new InlayToolpathParams()).GcodeLines) };
        yield return new object[] { "Rough3D", new Func<List<string>>(() => HeightfieldRoughEngine.Compute(Ridge(), new HeightfieldRoughParams()).GcodeLines) };
        yield return new object[] { "Finish3D", new Func<List<string>>(() => HeightfieldFinishEngine.Compute(Ridge(), new HeightfieldFinishParams()).GcodeLines) };
        yield return new object[] { "Moulding", new Func<List<string>>(() => MouldingToolpathEngine.Compute(new MouldingToolpathParams { Rail1 = new List<VectorPoint> { new(0, 0), new(20, 0) }, Rail2 = new List<VectorPoint> { new(0, 8), new(20, 8) } }).GcodeLines) };
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Engine_Produces_Real_Gcode(string name, Func<List<string>> run)
    {
        var lines = run();
        Assert.True(lines.Count >= 5, $"{name} produced only {lines.Count} lines — possible stub");
        Assert.Contains(lines, l => l.StartsWith("G0") || l.StartsWith("G1"));
        Assert.Contains(lines, l => l.StartsWith("M30") || l.StartsWith("M2") || l.StartsWith("M5"));
    }
}
