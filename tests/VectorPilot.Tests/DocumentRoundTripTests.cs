using System.IO;
using VectorPilot.Engine;
using VectorPilot.Engine.IO;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

public class DocumentRoundTripTests
{
    private static Job MakeJob()
    {
        var job = Job.CreateDefault();
        job.Name = "Sign Job";
        var sheet = job.ActiveSheet;
        sheet.Name = "Face 1";
        sheet.Width = 12; sheet.Height = 24; sheet.Thickness = 0.75;
        sheet.Material = Material.Oak();
        var layer = sheet.ActiveLayer;
        layer.Name = "Vectors";
        layer.AddShape(VectorShape.Rectangle(0.5, 0.5, 10, 20));
        layer.AddShape(VectorShape.Circle(new VectorPoint(6, 12), 2.5));
        var layer2 = sheet.AddLayer("Guide");
        layer2.AddShape(VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(12, 24)));
        return job;
    }

    [Fact]
    public void Save_Then_Load_RoundTrips_Job()
    {
        var job = MakeJob();
        var toolpaths = new List<PersistedToolpath>
        {
            new() { Name = "Profile 1", Strategy = "Profile", CutDepth = 0.25, FeedRate = 100 }
        };

        string dir = Path.Combine(Path.GetTempPath(), $"vp-doc-{Guid.NewGuid():N}");
        try
        {
            DocumentSaver.Save(job, toolpaths, dir);
            Assert.True(Directory.Exists(dir + ".shoppilot"));
            Assert.True(File.Exists(Path.Combine(dir + ".shoppilot", "manifest.json")));
            Assert.True(File.Exists(Path.Combine(dir + ".shoppilot", "toolpaths.json")));

            var result = DocumentLoader.Load(dir);
            Assert.Null(result.Error);
            Assert.NotNull(result.Job);
            Assert.Equal("Sign Job", result.Job!.Name);
            Assert.Single(result.Job.Sheets);
            var sheet = result.Job.ActiveSheet;
            Assert.Equal("Face 1", sheet.Name);
            Assert.Equal(12, sheet.Width);
            Assert.Equal(24, sheet.Height);
            Assert.Equal(0.75, sheet.Thickness);
            Assert.Equal("Oak", sheet.Material!.Name);
            Assert.Equal(2, sheet.Layers.Count);

            var vectors = sheet.Layers[0];
            Assert.Equal("Vectors", vectors.Name);
            Assert.Equal(2, vectors.Shapes.Count);
            Assert.Equal(ShapeType.Rectangle, vectors.Shapes[0].Type);
            Assert.Equal(10, vectors.Shapes[0].Bounds().Width, 3);
            Assert.Equal(ShapeType.Circle, vectors.Shapes[1].Type);
            Assert.Equal(2.5, vectors.Shapes[1].Radius, 3);

            Assert.NotNull(result.Toolpaths);
            Assert.Single(result.Toolpaths!);
            Assert.Equal("Profile 1", result.Toolpaths![0].Name);
        }
        finally
        {
            if (Directory.Exists(dir + ".shoppilot")) Directory.Delete(dir + ".shoppilot", recursive: true);
        }
    }

    [Fact]
    public void Manifest_Uses_Version_0_2()
    {
        var job = MakeJob();
        string dir = Path.Combine(Path.GetTempPath(), $"vp-doc-{Guid.NewGuid():N}");
        try
        {
            DocumentSaver.Save(job, Array.Empty<PersistedToolpath>(), dir);
            var manifest = System.Text.Json.JsonSerializer.Deserialize<ShopPilotManifest>(
                File.ReadAllText(Path.Combine(dir + ".shoppilot", "manifest.json")),
                DocumentJson.Options);
            Assert.Equal("0.2", manifest!.Version);
            Assert.Equal(1, manifest.SheetCount);
            Assert.Equal(job.Id.ToString(), manifest.Id);
        }
        finally
        {
            if (Directory.Exists(dir + ".shoppilot")) Directory.Delete(dir + ".shoppilot", recursive: true);
        }
    }

    [Fact]
    public void Load_Missing_Package_Reports_Error()
    {
        var result = DocumentLoader.Load(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid()));
        Assert.Null(result.Job);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public void Sheet_Dto_Uses_Mac_Width_Depth_Height_Keys()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(DocumentJson.ToSheet(MakeJob().ActiveSheet), DocumentJson.Options);
        Assert.Contains("\"width\"", json);
        Assert.Contains("\"depth\"", json);
        Assert.Contains("\"height\"", json);
        Assert.DoesNotContain("\"thickness\"", json);
    }
}
