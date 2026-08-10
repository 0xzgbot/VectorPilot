using System.IO;
using VectorPilot.Engine;
using VectorPilot.Serial;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>SPK-0603 parity: the dirty-toolpath export gate.</summary>
public class DirtyExportGateTests
{
    private static Toolpath CleanTp(string name)
    {
        var tp = new Toolpath { Name = name, IsDirty = false };
        tp.GCode.AddRange(new[] { "G0 X0 Y0", "M30" });
        return tp;
    }

    [Fact]
    public void Dirty_Toolpath_Is_Skipped_With_Warning()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vp-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "out.tap");
            var result = TapExporter.ExportWithGate(path, new[] { CleanTp("Clean"), new Toolpath { Name = "Dirty", IsDirty = true } });
            Assert.Contains(result.Warnings, w => w.Contains("Dirty"));
            Assert.DoesNotContain(result.Warnings, w => w.Contains("Clean"));
            var text = File.ReadAllText(result.Path);
            Assert.Contains("Clean", text);
            Assert.DoesNotContain("(Toolpath: Dirty", text);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void All_Dirty_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            TapExporter.ExportWithGate(Path.Combine(Path.GetTempPath(), "x.tap"), new[] { new Toolpath { Name = "D", IsDirty = true } }));
    }

    [Fact]
    public void All_Clean_Exports_Without_Warnings()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vp-gate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var result = TapExporter.ExportWithGate(Path.Combine(dir, "out.tap"), new[] { CleanTp("A"), CleanTp("B") });
            Assert.Empty(result.Warnings);
            Assert.True(File.Exists(result.Path));
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}

/// <summary>SPK-0418 parity: large-file stream stress on the simulator
/// (line-by-line ok-wait makes this inherently slow — 1000 lines ≈ 20s;
/// the point is proving zero line loss, not throughput).</summary>
public class LargeFileStreamStressTests
{
    [Fact]
    [Trait("Category", "Stress")]
    public async Task Thousand_Lines_Stream_Without_Loss()
    {
        await using var sim = new SimulatorTransport();
        await sim.OpenAsync(MachineProfile.Simulator());

        int oks = 0;
        sim.EventReceived += evt => { if (evt.Type == TransportEventType.Ok) oks++; };

        var lines = new List<string> { "G21", "G90" };
        for (int i = 0; i < 1000; i++)
        {
            lines.Add($"G1 X{(i % 100) / 10.0:0.0} Y{(i % 50) / 10.0:0.0} F2000");
        }
        lines.Add("M30");

        var streamer = new GCodeStreamer(sim);
        streamer.LineTimeout = TimeSpan.FromSeconds(30);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await streamer.StartAsync(lines, cts.Token);
        await Task.Delay(1500); // drain the virtual GRBL

        Assert.Equal(lines.Count, oks);
        Assert.Equal(StreamPhase.Completed, streamer.Phase);
        Assert.Equal(lines.Count, streamer.CurrentLine);
    }
}
