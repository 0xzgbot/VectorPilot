using System.Globalization;
using System.IO;
using VectorPilot.App;
using VectorPilot.Engine;
// DxfImporter sits in the Import/ FOLDER but declares namespace VectorPilot.Engine,
// so there is no VectorPilot.Engine.Import namespace to import.
using VectorPilot.Engine.IO;
using VectorPilot.Geometry;
using VectorPilot.Serial;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// A real job from a real DXF file, driven through the same classes the panels use:
/// import -> nest -> profile + pocket -> post GRBL mm -> simulator connect -> stream -> E-stop.
///
/// Every earlier end-to-end test built its geometry in code. This one starts from a file on
/// disk, which is how an actual user starts, and is the only test that would catch an importer
/// that produces shapes no strategy can cut.
/// </summary>
public class DxfJobE2ETests
{
    private static string FixtureDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
                dir = dir.Parent;
            return Path.Combine(dir!.FullName, "tests", "VectorPilot.Tests", "fixtures", "dxf");
        }
    }

    private static List<VectorShape> ImportBracket()
        => DxfImporter.Parse(File.ReadAllText(Path.Combine(FixtureDir, "bracket.dxf")));

    private static readonly StrategyRegistry Reg = new();

    /// <summary>
    /// A cutting move. Posted programs carry line numbers ("N40 G1 X…"), so a bare
    /// StartsWith("G1") silently matches nothing once a post has been applied.
    /// </summary>
    private static bool IsCut(string line)
    {
        var s = line.TrimStart();
        if (s.Length > 0 && (s[0] == 'N' || s[0] == 'n'))
        {
            int sp = s.IndexOf(' ');
            if (sp > 0) s = s[(sp + 1)..].TrimStart();
        }
        return s.StartsWith("G1") || s.StartsWith("G2") || s.StartsWith("G3");
    }

    // ---- 1. the fixture imports ----

    [Fact]
    public void The_Fixture_Exists_On_Disk()
    {
        Assert.True(File.Exists(Path.Combine(FixtureDir, "bracket.dxf")),
            $"missing fixture in {FixtureDir}");
    }

    [Fact]
    public void The_Dxf_Imports_Every_Entity()
    {
        var shapes = ImportBracket();

        // One closed LWPOLYLINE outline, two CIRCLE holes, one LINE mark.
        Assert.Equal(4, shapes.Count);
        Assert.Equal(2, shapes.Count(s => s.Type == ShapeType.Circle));
    }

    [Fact]
    public void The_Imported_Outline_Is_Closed_And_The_Right_Size()
    {
        var outline = ImportBracket().First(s => s.Type == ShapeType.Polyline);

        Assert.True(outline.Closed, "the LWPOLYLINE closed flag (70=1) was lost on import");
        Assert.Equal(120.0, outline.Points.Max(p => p.X) - outline.Points.Min(p => p.X), 3);
        Assert.Equal(80.0, outline.Points.Max(p => p.Y) - outline.Points.Min(p => p.Y), 3);
    }

    [Fact]
    public void The_Holes_Land_Where_The_File_Says()
    {
        var holes = ImportBracket().Where(s => s.Type == ShapeType.Circle).ToList();

        Assert.Contains(holes, h => Math.Abs(h.Points[0].X - 20) < 1e-6);
        Assert.Contains(holes, h => Math.Abs(h.Points[0].X - 100) < 1e-6);
        Assert.All(holes, h => Assert.Equal(4.0, h.Radius, 3));
    }

    // ---- 2. nest ----

    [Fact]
    public void The_Imported_Outline_Can_Be_Nested()
    {
        var outline = ImportBracket().First(s => s.Type == ShapeType.Polyline);

        var result = NestingEngine.Nest(new[] { outline }, 600, 400, margin: 10, spacing: 5);

        Assert.Single(result.Parts);
        Assert.InRange(result.Parts[0].Position.X, 0, 600);
        Assert.InRange(result.Parts[0].Position.Y, 0, 400);
    }

    // ---- 3. profile + pocket both cut the imported geometry ----

    [Fact]
    public void Profile_Cuts_The_Imported_Outline()
    {
        var outline = ImportBracket().First(s => s.Type == ShapeType.Polyline);
        var entry = Reg.Find("profile")!;

        Assert.Null(JobGate.AreaStrategyBlocker("profile", "Profile", new[] { outline }));

        var result = entry.Compute(new[] { outline }, null, entry.DefaultsJson);

        Assert.Null(result.Error);
        Assert.Contains(result.Gcode, IsCut);
    }

    [Fact]
    public void Pocket_Cuts_The_Imported_Outline()
    {
        var outline = ImportBracket().First(s => s.Type == ShapeType.Polyline);
        var entry = Reg.Find("pocket")!;

        var result = entry.Compute(new[] { outline }, null, entry.DefaultsJson);

        Assert.Null(result.Error);
        Assert.Contains(result.Gcode, IsCut);
    }

    [Fact]
    public void The_Bare_Line_Is_Refused_By_An_Area_Strategy()
    {
        // The MARK line is open: profiling it would emit junk. This is the gate both
        // Calculate and Machine Start consult.
        var mark = ImportBracket().First(s => s.Type == ShapeType.Line);

        Assert.NotNull(JobGate.AreaStrategyBlocker("pocket", "Pocket", new[] { mark }));
    }

    [Fact]
    public void The_Profile_Stays_Within_The_Sheet()
    {
        var outline = ImportBracket().First(s => s.Type == ShapeType.Polyline);
        var entry = Reg.Find("profile")!;
        var gcode = entry.Compute(new[] { outline }, null, entry.DefaultsJson).Gcode;

        foreach (var line in gcode.Where(IsCut))
            foreach (var tok in line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                if (tok.Length > 1 && char.ToUpperInvariant(tok[0]) == 'X'
                    && double.TryParse(tok[1..], NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
                    Assert.InRange(x, -20, 140);   // 120mm part plus tool radius and lead-in
    }

    // ---- 4. post to GRBL mm ----

    [Fact]
    public void The_Program_Posts_To_Grbl_Millimetres()
    {
        var outline = ImportBracket().First(s => s.Type == ShapeType.Polyline);
        var entry = Reg.Find("profile")!;
        var gcode = entry.Compute(new[] { outline }, null, entry.DefaultsJson).Gcode;

        // Real API: the GRBL template comes from PostTemplates.Grbl(units), and the engine
        // method is Emit(gcode, template) returning an EmitResult — not Apply(post, gcode),
        // and ShippedPostCatalog is a STATIC class with no All() instance method.
        var post = PostTemplate.Grbl(GCodeUnits.Millimeter);
        var posted = PostTemplateEngine.Emit(gcode, post).Lines;

        Assert.Contains(posted, l => l.Contains("G21"));      // millimetres, not G20
        Assert.DoesNotContain(posted, l => l.TrimStart().StartsWith("G20"));
        Assert.Contains(posted, IsCut);
    }

    [Fact]
    public void The_Posted_Program_Passes_The_Stream_Gate()
    {
        var outline = ImportBracket().First(s => s.Type == ShapeType.Polyline);
        var entry = Reg.Find("profile")!;
        var gcode = entry.Compute(new[] { outline }, null, entry.DefaultsJson).Gcode;

        Assert.Null(JobGate.StreamBlocker(gcode));
    }

    // ---- 5. simulator connect, stream, E-stop ----

    [Fact]
    public async Task The_Job_Streams_To_The_Simulator_And_E_Stops()
    {
        var outline = ImportBracket().First(s => s.Type == ShapeType.Polyline);
        var entry = Reg.Find("profile")!;
        var gcode = entry.Compute(new[] { outline }, null, entry.DefaultsJson).Gcode;

        // The same session class MachinePanel constructs.
        var session = new MachineSession(new SimulatorTransport());

        await session.ConnectAsync(new MachineProfile { Name = "Sim" });
        Assert.True(session.IsConnected, "the simulator did not connect");

        await session.StartStreamAsync(gcode);

        await session.EmergencyStopAsync();
        Assert.False(session.IsStreaming, "E-stop did not halt the stream");
    }

    [Fact]
    public async Task E_Stop_Works_Even_Before_A_Stream_Starts()
    {
        // Safety chrome is never gated on stream state.
        var session = new MachineSession(new SimulatorTransport());
        await session.ConnectAsync(new MachineProfile { Name = "Sim" });

        await session.EmergencyStopAsync();

        Assert.False(session.IsStreaming);
    }

    // ---- 6. the whole job survives a save/load ----

    [Fact]
    public void The_Imported_Job_Round_Trips_Through_A_Document()
    {
        var dir = Path.Combine(Path.GetTempPath(), "vp-dxf-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var job = new Job { Name = "Bracket" };
            foreach (var s in ImportBracket()) job.ActiveSheet.ActiveLayer.AddShape(s);

            var outline = job.ActiveSheet.ActiveLayer.Shapes.First(s => s.Type == ShapeType.Polyline);
            var entry = Reg.Find("profile")!;

            var set = new ToolpathTree();
            var tp = set.Add(ToolpathStrategy.Profile);
            tp.StrategyKey = "profile";
            tp.ParamsJson = entry.DefaultsJson;
            tp.GCode.AddRange(entry.Compute(new[] { outline }, null, entry.DefaultsJson).Gcode);

            var path = Path.Combine(dir, "bracket.shoppilot");
            DocumentSaver.Save(job, set.Toolpaths.Select(ToolpathPersistence.ToPersisted), path);

            var loaded = DocumentLoader.Load(path);

            Assert.NotNull(loaded.Job);
            Assert.Equal(4, loaded.Job!.ActiveSheet.ActiveLayer.Shapes.Count);
            Assert.NotNull(loaded.Toolpaths);
            Assert.Single(loaded.Toolpaths!);
            Assert.Equal("profile", loaded.Toolpaths![0].StrategyKey);
            Assert.NotEmpty(loaded.Toolpaths[0].GCode);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }
}
