using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using VectorPilot.Serial;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Whole-product E2E (SPK-0600/0601 spirit): a real job with vectors flows
/// through the strategy registry → post-processor → simulator stream to
/// completion — every stage wired end to end, no stubs.
/// </summary>
public class FullFlowE2ETests
{
    [Fact]
    public async Task Job_To_Machine_Complete_Flow()
    {
        // 1. Job with a closed rectangle on the active layer.
        var job = Job.CreateDefault();
        var layer = job.ActiveSheet.ActiveLayer;
        layer.Shapes.Add(VectorShape.Rectangle(0, 0, 50, 30));
        AppState.RestoreJob(job);

        // 2. Profile toolpath through the strategy registry.
        var registry = new StrategyRegistry();
        var entry = registry.Find("profile")!;
        var result = entry.Compute(layer.Shapes, null, "{}");
        Assert.True(result.Gcode.Count > 3, "profile produced G-code");

        var toolpath = new Toolpath { Name = "Outline", Strategy = ToolpathStrategy.Profile, IsDirty = false };
        toolpath.GCode.AddRange(result.Gcode);
        toolpath.EstimatedTimeSeconds = result.EstimatedTimeSeconds;
        AppState.Toolpaths.Toolpaths.Add(toolpath);

        // 3. Post-process through the v2 template engine (GRBL mm).
        var posted = PostTemplateEngine.Emit(toolpath.GCode, PostTemplate.Grbl(GCodeUnits.Millimeter));
        Assert.Contains(posted.Lines, l => l.Contains("G21")); // mm modal
        Assert.True(posted.MoveCount >= 4);                     // the rect profile

        // 4. Stream the posted program to the simulator and verify completion.
        await using var sim = new SimulatorTransport();
        await sim.OpenAsync(MachineProfile.Simulator());
        int oks = 0;
        sim.EventReceived += evt => { if (evt.Type == TransportEventType.Ok) oks++; };

        var streamer = new GCodeStreamer(sim);
        await streamer.StartAsync(posted.Lines);
        await Task.Delay(300);

        Assert.Equal(streamer.TotalLines, oks);          // every streamed line acked
        Assert.Equal(StreamPhase.Completed, streamer.Phase);
        Assert.True(sim.IsOpen);

        // 5. Export gate: dirty toolpath would be refused; this one is clean.
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vp-e2e-{Guid.NewGuid():N}");
        System.IO.Directory.CreateDirectory(dir);
        try
        {
            var exported = TapExporter.ExportWithGate(System.IO.Path.Combine(dir, "job.tap"), AppState.Toolpaths.Toolpaths);
            Assert.Empty(exported.Warnings);
            Assert.True(System.IO.File.Exists(exported.Path));
        }
        finally
        {
            System.IO.Directory.Delete(dir, true);
        }
    }
}
