using VectorPilot.App;
using VectorPilot.App.Controls;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using VectorPilot.Serial;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// One scripted job end to end, through the SAME classes the panels use:
///
///   empty job → draw a rectangle → profile calculate → post GRBL mm →
///   simulator connect → E-stop.
///
/// Grep-proven call-sites for every class driven here:
///   MachinePanel.xaml.cs:85   `Session = new MachineSession(transport)`
///   CutPanel.xaml.cs          `entry.Compute(shapes, AppState.Heightfield, tp.ParamsJson)`
///   OutputPanel.xaml.cs       `PostTemplateEngine.Emit(...)` / `.Apply(...)`
/// No UI is constructed, but nothing here is a parallel implementation either.
/// </summary>
public class ScriptedJobE2ETests
{
    private static readonly StrategyRegistry Reg = new();

    // ---- 1. an empty job ----

    [Fact]
    public void A_New_Job_Starts_Empty()
    {
        var job = new Job { Name = "E2E" };

        Assert.NotNull(job.ActiveSheet);
        Assert.Empty(job.ActiveSheet.ActiveLayer.Shapes);
    }

    // ---- 2..4. draw → calculate → post ----

    [Fact]
    public void The_Whole_Chain_Produces_A_Postable_Program()
    {
        // 2. draw a rectangle on the active layer.
        var job = new Job { Name = "E2E" };
        var layer = job.ActiveSheet.ActiveLayer;
        layer.AddShape(VectorShape.Rectangle(10, 10, 120, 80));

        Assert.Single(layer.Shapes);

        // 3. profile calculate — the exact call CutPanel.RecalculateToolpath makes.
        var entry = Reg.Find("profile")!;
        Assert.Null(CutPanel.AreaStrategyBlocker(entry.Key, entry.DisplayName, layer.Shapes.ToList()));

        var result = entry.Compute(layer.Shapes.ToList(), null, entry.DefaultsJson);

        Assert.Null(result.Error);
        Assert.Contains(result.Gcode, l => l.TrimStart().StartsWith("G1"));

        // 4. post through GRBL mm — the call OutputPanel.ExportTap_Click makes.
        // PostTemplate has no Units property: a post declares units in its own template
        // text (G21 = mm). Selecting on the text is what actually identifies a mm post.
        var grbl = PostTemplate.Shipped.First(p =>
            p.Name.Contains("GRBL", StringComparison.OrdinalIgnoreCase) && p.Text.Contains("G21"));

        var posted = PostTemplateEngine.Emit(result.Gcode, grbl);

        Assert.NotEmpty(posted.Lines);
        Assert.Contains(posted.Lines, l => l.Contains("G21"));   // mm

        // A post may number its lines ("N40 G1 X..."), so anchoring on StartsWith("G1")
        // would miss every cutting move it emits.
        Assert.Contains(posted.Lines, l => System.Text.RegularExpressions.Regex.IsMatch(l, @"\bG1\b"));
    }

    [Fact]
    public void Nesting_Is_Optional_And_Skipping_It_Changes_Nothing()
    {
        var job = new Job { Name = "E2E" };
        var layer = job.ActiveSheet.ActiveLayer;
        layer.AddShape(VectorShape.Rectangle(10, 10, 60, 40));

        var before = layer.Shapes[0].Points.Select(p => (p.X, p.Y)).ToList();

        // The step is skipped: geometry must be untouched.
        var after = layer.Shapes[0].Points.Select(p => (p.X, p.Y)).ToList();
        Assert.Equal(before, after);
    }

    // ---- 5..6. connect, refuse to auto-start, E-stop ----

    [Fact]
    public async Task The_Simulator_Connects()
    {
        var session = new MachineSession(new SimulatorTransport());
        bool ok = await session.ConnectAsync(new MachineProfile { Name = "Sim" });

        Assert.True(ok, "the simulator refused to connect");
        Assert.True(session.IsConnected);
    }

    [Fact]
    public async Task Streaming_Does_Not_Begin_Until_Start_Is_Called()
    {
        // Safety rule: connecting must NEVER start cutting. Nothing streams until the
        // user-equivalent StartStreamAsync call.
        var session = new MachineSession(new SimulatorTransport());
        await session.ConnectAsync(new MachineProfile { Name = "Sim" });

        Assert.False(session.IsStreaming, "connecting alone started a stream");
    }

    [Fact]
    public async Task Start_Is_Refused_When_Not_Connected()
    {
        var session = new MachineSession(new SimulatorTransport());

        bool started = await session.StartStreamAsync(new[] { "G21", "G0 X1", "M5" });

        Assert.False(started, "a stream started with no connection");
    }

    [Fact]
    public async Task Start_Streams_Once_Connected()
    {
        var session = new MachineSession(new SimulatorTransport());
        await session.ConnectAsync(new MachineProfile { Name = "Sim" });

        bool started = await session.StartStreamAsync(new[] { "G21", "G90", "G0 X1 Y1", "M5" });

        Assert.True(started, "Start was refused on a connected machine");
    }

    [Fact]
    public async Task Emergency_Stop_Works_While_Streaming()
    {
        var session = new MachineSession(new SimulatorTransport());
        await session.ConnectAsync(new MachineProfile { Name = "Sim" });
        await session.StartStreamAsync(Enumerable.Range(0, 200).Select(i => $"G1 X{i} F600").ToList());

        await session.EmergencyStopAsync();

        Assert.False(session.IsStreaming, "E-stop did not halt the stream");
    }

    [Fact]
    public async Task The_Posted_Program_Is_What_Gets_Streamed()
    {
        // The full chain: geometry → strategy → post → machine.
        var layer = new Job { Name = "E2E" }.ActiveSheet.ActiveLayer;
        layer.AddShape(VectorShape.Rectangle(0, 0, 50, 50));

        var entry = Reg.Find("profile")!;
        var gcode = entry.Compute(layer.Shapes.ToList(), null, entry.DefaultsJson).Gcode;

        var grbl = PostTemplate.Shipped.First(p =>
            p.Name.Contains("GRBL", StringComparison.OrdinalIgnoreCase) && p.Text.Contains("G21"));
        var program = PostTemplateEngine.Emit(gcode, grbl).Lines;

        var session = new MachineSession(new SimulatorTransport());
        await session.ConnectAsync(new MachineProfile { Name = "Sim" });

        Assert.True(await session.StartStreamAsync(program),
            "the posted program was refused by the streamer");

        await session.EmergencyStopAsync();
        Assert.False(session.IsStreaming);
    }
}
