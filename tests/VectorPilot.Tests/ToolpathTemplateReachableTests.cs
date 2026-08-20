using System.IO;
using System.Text.Json;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Toolpath templates — the manager CutPanel's Save as… / Apply buttons drive.
///
/// ToolpathTemplateManager had zero VectorPilot.App call-sites: templates could be saved
/// and loaded by the engine but no UI could create or apply one.
/// </summary>
public class ToolpathTemplateReachableTests : IDisposable
{
    private readonly string _path;

    public ToolpathTemplateReachableTests()
        => _path = Path.Combine(Path.GetTempPath(), $"vp-templates-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    private ToolpathTemplateManager Manager() => new(_path);

    private const string PocketParams =
        """{"cutDepthMm":6.5,"stepOverPercent":45,"feedRateMmPerMin":1800,"contourFirst":true}""";

    // ---- round-trip through JSON ----

    [Fact]
    public void A_Saved_Template_Survives_A_Reload()
    {
        var saved = Manager().SaveTemplate("Ply 6mm", ToolpathTemplateType.Pocket, PocketParams, "pocket");

        // A NEW manager reads the file from disk — that is the round-trip that matters.
        var reloaded = Manager().Templates;

        Assert.Single(reloaded);
        Assert.Equal("Ply 6mm", reloaded[0].Name);
        Assert.Equal(PocketParams, reloaded[0].ParamsJson);
        Assert.Equal(saved.Id, reloaded[0].Id);
    }

    [Fact]
    public void The_Registry_Key_Round_Trips()
    {
        // ToolpathTemplateType only covers 5 of the 24 registered strategies, so the key
        // is what makes a template usable for the rest.
        Manager().SaveTemplate("Photo fine", ToolpathTemplateType.VCarve, """{"a":1}""", "photo-vcarve");

        Assert.Equal("photo-vcarve", Manager().Templates[0].StrategyKey);
    }

    [Fact]
    public void The_Params_Json_Is_Still_Valid_Json_After_A_Round_Trip()
    {
        Manager().SaveTemplate("Ply 6mm", ToolpathTemplateType.Pocket, PocketParams, "pocket");

        var doc = JsonDocument.Parse(Manager().Templates[0].ParamsJson);
        Assert.Equal(6.5, doc.RootElement.GetProperty("cutDepthMm").GetDouble());
        Assert.True(doc.RootElement.GetProperty("contourFirst").GetBoolean());
    }

    [Fact]
    public void Several_Templates_Coexist()
    {
        var m = Manager();
        m.SaveTemplate("Shallow", ToolpathTemplateType.Pocket, """{"cutDepthMm":2}""", "pocket");
        m.SaveTemplate("Deep", ToolpathTemplateType.Pocket, """{"cutDepthMm":18}""", "pocket");

        Assert.Equal(2, Manager().Templates.Count);
    }

    [Fact]
    public void A_Deleted_Template_Stays_Deleted()
    {
        var m = Manager();
        var t = m.SaveTemplate("Temp", ToolpathTemplateType.Profile, "{}", "profile");
        m.DeleteTemplate(t.Id);

        Assert.Empty(Manager().Templates);
    }

    [Fact]
    public void A_Missing_File_Loads_As_Empty_Not_A_Crash()
    {
        Assert.Empty(new ToolpathTemplateManager(
            Path.Combine(Path.GetTempPath(), $"vp-none-{Guid.NewGuid():N}.json")).Templates);
    }

    // ---- filtering by strategy ----

    [Fact]
    public void Templates_Are_Offered_For_Their_Own_Strategy()
    {
        var m = Manager();
        m.SaveTemplate("Pocket A", ToolpathTemplateType.Pocket, "{}", "pocket");
        m.SaveTemplate("Thread A", ToolpathTemplateType.Profile, "{}", "threadmill");

        var forPocket = m.ForStrategy("pocket");
        Assert.Contains(forPocket, t => t.Name == "Pocket A");
        Assert.DoesNotContain(forPocket, t => t.Name == "Thread A");
    }

    [Fact]
    public void Legacy_Templates_Without_A_Key_Are_Offered_Everywhere()
    {
        // Saved before StrategyKey existed: still usable rather than orphaned.
        Manager().SaveTemplate("Old", ToolpathTemplateType.Profile, "{}");

        Assert.Contains(Manager().ForStrategy("pocket"), t => t.Name == "Old");
        Assert.Contains(Manager().ForStrategy("threadmill"), t => t.Name == "Old");
    }

    [Fact]
    public void Strategy_Matching_Ignores_Case()
    {
        Manager().SaveTemplate("Case", ToolpathTemplateType.Pocket, "{}", "Pocket");

        Assert.Contains(Manager().ForStrategy("pocket"), t => t.Name == "Case");
    }

    // ---- applying changes params BEFORE Calculate ----

    [Fact]
    public void Applying_A_Template_Changes_ParamsJson()
    {
        var template = Manager().SaveTemplate("Ply 6mm", ToolpathTemplateType.Pocket, PocketParams, "pocket");

        var tp = new Toolpath { Name = "Pocket 1", ParamsJson = """{"cutDepthMm":1}""" };
        string before = tp.ParamsJson;

        // Exactly what ApplyTemplate_Click does.
        tp.ParamsJson = template.ParamsJson;
        tp.IsDirty = true;

        Assert.NotEqual(before, tp.ParamsJson);
        Assert.Equal(PocketParams, tp.ParamsJson);
        Assert.True(tp.IsDirty, "the toolpath must be marked dirty so Calculate regenerates");
    }

    [Fact]
    public void The_Applied_Params_Actually_Reach_The_Strategy()
    {
        var reg = new VectorPilot.App.StrategyRegistry();
        var entry = reg.Find("pocket")!;
        var shapes = new[] { VectorPilot.Geometry.VectorShape.Rectangle(0, 0, 80, 60) };

        var shallow = entry.Compute(shapes, null, """{"cutDepthMm":1,"stepDownMm":1}""");
        var deep = entry.Compute(shapes, null, """{"cutDepthMm":12,"stepDownMm":2}""");

        Assert.NotEqual(
            string.Join("\n", shallow.Gcode),
            string.Join("\n", deep.Gcode));
    }
}
