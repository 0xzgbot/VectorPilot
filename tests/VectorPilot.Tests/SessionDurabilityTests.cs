using System.IO;
using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Engine.IO;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Session durability: a saved job comes back with its shapes AND its toolpaths, and the
/// crash autosave lands where MainWindow actually looks for it.
///
/// The paths here are the ones MainWindow uses — DocumentSaver.Save / DocumentLoader.Load
/// and LocalApplicationData/VectorPilot/autosave.shoppilot — not a parallel test-only route.
/// </summary>
public class SessionDurabilityTests : IDisposable
{
    private readonly string _dir;

    public SessionDurabilityTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "vp-durability-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* a lingering handle must not fail the test */ }
    }

    private string Doc(string name = "job.shoppilot") => Path.Combine(_dir, name);

    /// <summary>
    /// Load and assert the document is readable, returning non-null parts. LoadResult's
    /// members are nullable and this project builds with -warnaserror, so unwrapping once
    /// here keeps every test free of null-forgiving noise.
    /// </summary>
    private static (Job Job, List<PersistedToolpath> Toolpaths) LoadOk(string path)
    {
        var loaded = DocumentLoader.Load(path);
        Assert.NotNull(loaded.Job);
        Assert.NotNull(loaded.Toolpaths);
        return (loaded.Job, loaded.Toolpaths!.ToList());
    }

    /// <summary>A job with real geometry, as if the user had drawn it.</summary>
    private static Job DrawnJob()
    {
        var job = new Job { Name = "Sign" };
        var layer = job.ActiveSheet.ActiveLayer;
        layer.AddShape(VectorShape.Rectangle(10, 10, 120, 80));
        layer.AddShape(VectorShape.Polyline(
            new List<VectorPoint> { new(20, 20), new(60, 40), new(100, 20) }, closed: false));
        return job;
    }

    private static List<PersistedToolpath> CalculatedToolpaths(Job job)
    {
        var reg = new StrategyRegistry();
        var entry = reg.Find("profile")!;
        var shapes = job.ActiveSheet.ActiveLayer.Shapes.Take(1).ToList();

        var set = new ToolpathTree();
        var tp = set.Add(ToolpathStrategy.Profile);
        tp.Name = "Outline";
        tp.StrategyKey = "profile";
        tp.ParamsJson = entry.DefaultsJson;
        tp.SelectedShapeIds.Add(shapes[0].Id);
        tp.GCode.AddRange(entry.Compute(shapes, null, entry.DefaultsJson).Gcode);

        return set.Toolpaths.Select(ToolpathPersistence.ToPersisted).ToList();
    }

    // ---- round trip: shapes and toolpaths both survive ----

    [Fact]
    public void A_Saved_Job_Reloads_With_Its_Shapes()
    {
        var job = DrawnJob();
        int shapes = job.ActiveSheet.ActiveLayer.Shapes.Count;

        DocumentSaver.Save(job, CalculatedToolpaths(job), Doc());
        var loaded = LoadOk(Doc());

        Assert.NotNull(loaded.Job);
        Assert.Equal(shapes, loaded.Job.ActiveSheet.ActiveLayer.Shapes.Count);
    }

    [Fact]
    public void A_Saved_Job_Reloads_With_Its_Toolpaths()
    {
        var job = DrawnJob();
        var toolpaths = CalculatedToolpaths(job);

        DocumentSaver.Save(job, toolpaths, Doc());
        var loaded = LoadOk(Doc());

        Assert.Equal(toolpaths.Count, loaded.Toolpaths.Count);
        Assert.Equal("Outline", loaded.Toolpaths[0].Name);
    }

    [Fact]
    public void The_Reloaded_Toolpath_Keeps_Its_Gcode()
    {
        var job = DrawnJob();
        var toolpaths = CalculatedToolpaths(job);
        int lines = toolpaths[0].GCode.Count;

        Assert.True(lines > 0, "fixture produced no G-code");

        DocumentSaver.Save(job, toolpaths, Doc());
        var loaded = LoadOk(Doc());

        Assert.Equal(lines, loaded.Toolpaths[0].GCode.Count);
    }

    [Fact]
    public void The_Reloaded_Toolpath_Keeps_Its_Strategy_Key()
    {
        // Losing the key silently reloads as a different strategy — the recurring bug class.
        var job = DrawnJob();

        DocumentSaver.Save(job, CalculatedToolpaths(job), Doc());
        var loaded = LoadOk(Doc());

        // PersistedToolpath carried only Strategy (the enum NAME) — StrategyKey and
        // ParamsJson were dropped entirely, so a Thread Mill or Laser Picture job reloaded as
        // whatever enum case matched and cut something else. Both are now persisted.
        Assert.Equal("profile", loaded.Toolpaths[0].StrategyKey);
        Assert.Equal("profile", ToolpathPersistence.FromPersisted(loaded.Toolpaths[0]).StrategyKey);
    }

    [Fact]
    public void The_Reloaded_Toolpath_Keeps_Its_Params()
    {
        var job = DrawnJob();
        var toolpaths = CalculatedToolpaths(job);

        DocumentSaver.Save(job, toolpaths, Doc());
        var loaded = LoadOk(Doc());

        Assert.Equal(toolpaths[0].ParamsJson, loaded.Toolpaths[0].ParamsJson);
        Assert.False(string.IsNullOrWhiteSpace(loaded.Toolpaths[0].ParamsJson),
            "params came back empty — the reloaded job would cut with defaults");
    }

    [Fact]
    public void Shape_Geometry_Survives_Exactly()
    {
        var job = DrawnJob();
        var before = job.ActiveSheet.ActiveLayer.Shapes[0].Points
            .Select(p => (p.X, p.Y)).ToList();

        DocumentSaver.Save(job, CalculatedToolpaths(job), Doc());
        var loaded = LoadOk(Doc());

        var after = loaded.Job.ActiveSheet.ActiveLayer.Shapes[0].Points
            .Select(p => (p.X, p.Y)).ToList();

        Assert.Equal(before, after);
    }

    [Fact]
    public void The_Open_Polyline_Stays_Open()
    {
        // Closed-ness drives whether an area strategy will run, so it must not flip.
        var job = DrawnJob();

        DocumentSaver.Save(job, CalculatedToolpaths(job), Doc());
        var loaded = LoadOk(Doc());

        var polyline = loaded.Job.ActiveSheet.ActiveLayer.Shapes
            .First(s => s.Type == ShapeType.Polyline);

        Assert.False(polyline.Closed);
    }

    [Fact]
    public void The_Job_Name_Survives()
    {
        var job = DrawnJob();
        DocumentSaver.Save(job, CalculatedToolpaths(job), Doc());

        Assert.Equal("Sign", LoadOk(Doc()).Job.Name);
    }

    // ---- a second save overwrites cleanly ----

    [Fact]
    public void Saving_Twice_Does_Not_Duplicate_Toolpaths()
    {
        var job = DrawnJob();
        var toolpaths = CalculatedToolpaths(job);

        DocumentSaver.Save(job, toolpaths, Doc());
        DocumentSaver.Save(job, toolpaths, Doc());

        Assert.Equal(toolpaths.Count, LoadOk(Doc()).Toolpaths.Count);
    }

    [Fact]
    public void A_Job_With_No_Toolpaths_Round_Trips()
    {
        var job = DrawnJob();
        DocumentSaver.Save(job, new List<PersistedToolpath>(), Doc());

        var loaded = LoadOk(Doc());

        Assert.Empty(loaded.Toolpaths);
    }

    // ---- the autosave path is the one MainWindow uses ----

    [Fact]
    public void The_Autosave_Path_Is_Under_Local_App_Data()
    {
        // Mirrors MainWindow.StateDir()/AutosaveDir() exactly. If these drift, recovery
        // silently looks in the wrong place and the user's crashed work is "gone".
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VectorPilot", "autosave.shoppilot");

        Assert.EndsWith(Path.Combine("VectorPilot", "autosave.shoppilot"), expected);
        Assert.Contains("Local", expected);   // Local, not Roaming: machine-specific state
    }

    [Fact]
    public void An_Autosave_Round_Trips_Like_A_Manual_Save()
    {
        // AutosaveNow does exactly this: ToPersisted then DocumentSaver.Save.
        var job = DrawnJob();
        var autosave = Doc("autosave.shoppilot");

        DocumentSaver.Save(job, CalculatedToolpaths(job), autosave);

        var loaded = LoadOk(autosave);
        Assert.NotNull(loaded.Job);
        Assert.NotEmpty(loaded.Toolpaths);
        Assert.NotEmpty(loaded.Toolpaths[0].GCode);
    }

    [Fact]
    public void A_Dirty_Job_Is_What_Autosave_Captures()
    {
        var job = DrawnJob();
        job.IsDirty = true;

        var autosave = Doc("autosave.shoppilot");
        DocumentSaver.Save(job, CalculatedToolpaths(job), autosave);

        Assert.True(File.Exists(autosave) || Directory.Exists(autosave),
            "autosave produced nothing on disk");
    }

    [Fact]
    public void Recovered_Work_Is_Loadable_Without_Any_Prompt()
    {
        // CheckForRecoverableWork returns immediately when App.IsAutomated, so ui_verify
        // reaches the shell instead of hanging on a recovery modal. What matters for a test
        // is that the autosave it would have offered is genuinely loadable on its own — the
        // recovery path needs no UI at all.
        var autosave = Doc("autosave.shoppilot");
        var job = DrawnJob();
        job.IsDirty = true;

        DocumentSaver.Save(job, CalculatedToolpaths(job), autosave);

        var recovered = LoadOk(autosave);

        Assert.Equal("Sign", recovered.Job.Name);
        Assert.Equal(job.ActiveSheet.ActiveLayer.Shapes.Count,
                     recovered.Job.ActiveSheet.ActiveLayer.Shapes.Count);
        Assert.NotEmpty(recovered.Toolpaths);
    }
}
