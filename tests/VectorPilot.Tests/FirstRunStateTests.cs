using System.IO;
using VectorPilot.App;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// First-run welcome state (Mac SPK-UXPOLISH parity). The marker must make the
/// welcome appear exactly once, and a failure to read or write it must never
/// break startup.
/// </summary>
public class FirstRunStateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "vp-firstrun-" + Guid.NewGuid().ToString("N"));

    private string Marker => Path.Combine(_dir, "first-run.marker");

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
    }

    [Fact]
    public void A_Fresh_Install_Is_First_Run()
    {
        var s = new FirstRunState(Marker);
        Assert.True(s.IsFirstRun);
    }

    [Fact]
    public void Marking_Shown_Ends_First_Run()
    {
        var s = new FirstRunState(Marker);
        s.MarkShown();

        Assert.False(s.IsFirstRun);
        Assert.True(File.Exists(Marker));
    }

    [Fact]
    public void The_Marker_Survives_A_New_Instance()
    {
        new FirstRunState(Marker).MarkShown();

        // A later launch reads the same marker.
        Assert.False(new FirstRunState(Marker).IsFirstRun);
    }

    [Fact]
    public void MarkShown_Creates_Missing_Directories()
    {
        var nested = Path.Combine(_dir, "a", "b", "first-run.marker");
        new FirstRunState(nested).MarkShown();
        Assert.True(File.Exists(nested));
    }

    [Fact]
    public void Reset_Brings_Back_First_Run()
    {
        var s = new FirstRunState(Marker);
        s.MarkShown();
        Assert.False(s.IsFirstRun);

        s.Reset();
        Assert.True(s.IsFirstRun);
    }

    [Fact]
    public void Reset_On_A_Fresh_Install_Is_Harmless()
    {
        var s = new FirstRunState(Marker);
        s.Reset();
        Assert.True(s.IsFirstRun);
    }

    [Fact]
    public void Marking_Twice_Is_Idempotent()
    {
        var s = new FirstRunState(Marker);
        s.MarkShown();
        s.MarkShown();
        Assert.False(s.IsFirstRun);
    }

    [Fact]
    public void The_Marker_Records_A_Timestamp()
    {
        new FirstRunState(Marker).MarkShown();
        Assert.True(DateTime.TryParse(File.ReadAllText(Marker), out _));
    }
}
