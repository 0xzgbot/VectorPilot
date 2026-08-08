using System.IO;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class MaterialDatabaseTests
{
    private static MaterialDatabase NewDb(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), $"vp-mat-{Guid.NewGuid():N}.json");
        return new MaterialDatabase(path);
    }

    [Fact]
    public void Defaults_And_RoundTrip()
    {
        var db = NewDb(out var path);
        try
        {
            db.WithDefaults();
            Assert.True(db.Materials.Count >= 5);

            var reloaded = new MaterialDatabase(path);
            Assert.Equal(db.Materials.Count, reloaded.Materials.Count);
            Assert.Equal(16000, reloaded.Find("Softwood")!.RecommendedSpindleSpeed);

            reloaded.Add(new Material { Name = "Corian", RecommendedFeedRate = 700 });
            Assert.NotNull(new MaterialDatabase(path).Find("Corian"));
            Assert.True(reloaded.Delete("Corian"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Recommendations_Apply()
    {
        var db = NewDb(out var path);
        try
        {
            db.WithDefaults();
            double? feed = null, plunge = null, spindle = null;
            db.ApplyRecommendations(db.Find("Hardwood")!, (f, p, s) => { feed = f; plunge = p; spindle = s; });
            Assert.Equal(1200, feed);
            Assert.Equal(600, plunge);
            Assert.Equal(15000, spindle);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

public class PostCatalogTests
{
    private static PostCatalog NewCatalog(out string path)
    {
        path = Path.Combine(Path.GetTempPath(), $"vp-post-{Guid.NewGuid():N}.json");
        return new PostCatalog(path);
    }

    [Fact]
    public void Latest_Version_Tracking()
    {
        var cat = NewCatalog(out var path);
        try
        {
            cat.WithDefaults();
            var latest = cat.Latest("GRBL");
            Assert.NotNull(latest);
            Assert.Equal("V2", latest!.Version);

            // Installing V3 flips Latest.
            cat.Install(new PostDefinition { Name = "GRBL", Version = "V3", IsLatest = false });
            Assert.Equal("V3", cat.Latest("GRBL")!.Version);
            Assert.Equal(1, cat.Versions("GRBL").Count(v => v.IsLatest));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Remove_Deletes_Version()
    {
        var cat = NewCatalog(out var path);
        try
        {
            cat.WithDefaults();
            Assert.True(cat.Remove("GRBL", "V1"));
            Assert.False(cat.Remove("GRBL", "V9"));
            Assert.Equal(2, cat.Posts.Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}

public class SimulationPlaybackTests
{
    private static readonly List<string> Gcode = new()
    {
        "G21", "G90", "G0 X0 Y0", "G1 X10 Y0 F1000", "G1 X10 Y10 F1000", "G1 Z-2 F300"
    };

    [Fact]
    public void Steps_And_Tracks_Position()
    {
        var sim = new SimulationPlayback(Gcode);
        while (!sim.IsFinished) sim.Step();
        Assert.Equal(10.0, sim.PositionX, 6);
        Assert.Equal(10.0, sim.PositionY, 6);
        Assert.Equal(-2.0, sim.PositionZ, 6);
        Assert.Equal(1.0, sim.Progress, 6);
    }

    [Fact]
    public void Speed_Multiplier_Scales_StepMany()
    {
        var sim1 = new SimulationPlayback(Gcode, 1.0);
        var sim4 = new SimulationPlayback(Gcode, 4.0);
        Assert.Equal(4, sim1.StepMany(4));
        Assert.Equal(4, sim4.StepMany(1)); // 1 × 4x = 4 lines
    }

    [Fact]
    public void Restart_Resets_Position()
    {
        var sim = new SimulationPlayback(Gcode);
        sim.StepMany(3);
        Assert.Equal(0.0, sim.PositionX, 6);
        sim.Restart();
        Assert.Equal(0, sim.CurrentIndex);
        Assert.Equal(0.0, sim.PositionX, 6);
    }

    [Fact]
    public void Speed_Clamped_To_16x()
    {
        var sim = new SimulationPlayback(Gcode, 50);
        Assert.Equal(16.0, sim.SpeedMultiplier, 6);
    }
}

public class CommandRegistryTests
{
    [Fact]
    public void Search_And_Shortcuts()
    {
        var reg = new CommandRegistry();
        int ran = 0;
        reg.Register(new CommandRegistry.Command("save", "Save Job", "Ctrl+S", "File", () => ran++));
        reg.Register(new CommandRegistry.Command("calc", "Calculate Toolpaths", "F9", "Toolpath", () => ran++));

        Assert.Single(reg.Search("save"));
        Assert.Equal(2, reg.Search("").Count());
        Assert.NotNull(reg.ByShortcut("ctrl+s"));
        reg.ByShortcut("F9")!.Execute();
        Assert.Equal(1, ran);
    }
}
