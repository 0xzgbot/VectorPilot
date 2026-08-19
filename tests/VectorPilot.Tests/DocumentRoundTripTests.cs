using System.IO;
using System.Text.Json;
using VectorPilot.Engine;
using VectorPilot.Engine.IO;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Document round-trip against the REAL Mac fixtures in ../ShopPilot/fixtures/shoppilot.
///
/// A .shoppilot document is a bundle directory: manifest.json + toolpaths.json +
/// sheets/&lt;id&gt;.json. If VectorPilot cannot read what the Mac writes, "file
/// compatibility parity" is a claim with nothing behind it.
/// </summary>
public class DocumentRoundTripTests
{
    private static string FixtureDir
    {
        get
        {
            // tests run from bin/<cfg>/net8.0; walk up to the repo, then across.
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
                dir = dir.Parent;
            return dir is null
                ? ""
                : Path.GetFullPath(Path.Combine(dir.FullName, "..", "ShopPilot", "fixtures", "shoppilot"));
        }
    }

    private static string Fixture(string name) => Path.Combine(FixtureDir, name + ".shoppilot");

    public static IEnumerable<object[]> Fixtures()
    {
        yield return new object[] { "Sign" };
        yield return new object[] { "Calibration" };
    }

    // ---- the Mac's own documents must load ----

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Mac_Fixture_Exists_As_A_Bundle(string name)
    {
        string path = Fixture(name);
        Assert.True(Directory.Exists(path), $"missing Mac fixture {path}");
        Assert.True(File.Exists(Path.Combine(path, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(path, "toolpaths.json")));
        Assert.True(Directory.Exists(Path.Combine(path, "sheets")));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Mac_Fixture_Loads(string name)
    {
        var result = DocumentLoader.Load(Fixture(name));

        Assert.NotNull(result);
        Assert.NotNull(result.Job);
        Assert.False(string.IsNullOrWhiteSpace(result.Job!.Name));
    }

    [Fact]
    public void Sign_Fixture_Carries_Its_Sheet_And_Geometry()
    {
        var result = DocumentLoader.Load(Fixture("Sign"));

        var sheet = result.Job!.Sheets.FirstOrDefault();
        Assert.NotNull(sheet);
        Assert.True(sheet!.Layers.Count > 0, "sheet has no layers");
        Assert.True(sheet.Layers.Sum(l => l.Shapes.Count) > 0, "no vectors loaded");
    }

    [Fact]
    public void Mac_Manifest_Uses_The_Schema_Keys_We_Expect()
    {
        // Pin the actual Mac schema so a rename on either side fails loudly here.
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixture("Sign"), "manifest.json")));
        var root = doc.RootElement;

        foreach (var key in new[] { "id", "name", "createdAt", "updatedAt", "version", "sheetCount", "documentVariables" })
            Assert.True(root.TryGetProperty(key, out _), $"manifest is missing '{key}'");

        Assert.Equal("0.2", root.GetProperty("version").GetString());
    }

    [Fact]
    public void Mac_Sheet_Uses_The_Schema_Keys_We_Expect()
    {
        string sheetFile = Directory.GetFiles(Path.Combine(Fixture("Sign"), "sheets"), "*.json")[0];
        using var doc = JsonDocument.Parse(File.ReadAllText(sheetFile));
        var root = doc.RootElement;

        foreach (var key in new[] { "id", "name", "width", "height", "depth", "material", "layers", "isDoubleSided" })
            Assert.True(root.TryGetProperty(key, out _), $"sheet is missing '{key}'");

        var layer = root.GetProperty("layers")[0];
        foreach (var key in new[] { "id", "name", "isVisible", "isLocked", "vectors", "toolpathIds" })
            Assert.True(layer.TryGetProperty(key, out _), $"layer is missing '{key}'");
    }

    [Fact]
    public void Mac_Toolpath_Uses_The_Schema_Keys_We_Expect()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixture("Sign"), "toolpaths.json")));
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);

        var tp = doc.RootElement[0];
        foreach (var key in new[] { "id", "name", "paramsJSON", "estimatedTimeSeconds", "isDirty" })
            Assert.True(tp.TryGetProperty(key, out _), $"toolpath is missing '{key}'");
    }

    // ---- our own save must round-trip ----

    [Fact]
    public void Saving_And_Reloading_Preserves_The_Job()
    {
        var job = new Job { Name = "RoundTrip" };
        var sheet = job.ActiveSheet;
        sheet.Layers[0].AddShape(VectorShape.Rectangle(5, 5, 40, 25));
        sheet.Layers[0].AddShape(VectorShape.Circle(new VectorPoint(80, 40), 12));

        string path = Path.Combine(Path.GetTempPath(), $"vp-rt-{Guid.NewGuid():N}.shoppilot");
        try
        {
            DocumentSaver.Save(job, Array.Empty<PersistedToolpath>(), path);
            Assert.True(DocumentSaver.Exists(path));

            var back = DocumentLoader.Load(path);
            Assert.Equal("RoundTrip", back.Job!.Name);
            Assert.Equal(sheet.Layers[0].Shapes.Count,
                         back.Job.Sheets[0].Layers.Sum(l => l.Shapes.Count));
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void Our_Manifest_Matches_The_Mac_Schema()
    {
        var job = new Job { Name = "SchemaCheck" };
        string path = Path.Combine(Path.GetTempPath(), $"vp-schema-{Guid.NewGuid():N}.shoppilot");
        try
        {
            DocumentSaver.Save(job, Array.Empty<PersistedToolpath>(), path);

            using var ours = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "manifest.json")));
            using var mac = JsonDocument.Parse(File.ReadAllText(Path.Combine(Fixture("Sign"), "manifest.json")));

            // Every key the Mac writes must be present in ours, or the Mac cannot
            // open our documents.
            foreach (var macProp in mac.RootElement.EnumerateObject())
            {
                Assert.True(ours.RootElement.TryGetProperty(macProp.Name, out _),
                    $"our manifest omits the Mac key '{macProp.Name}'");
            }
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void Our_Sheet_Matches_The_Mac_Schema()
    {
        var job = new Job { Name = "SheetSchema" };
        job.ActiveSheet.Layers[0].AddShape(VectorShape.Rectangle(0, 0, 10, 10));

        string path = Path.Combine(Path.GetTempPath(), $"vp-sheet-{Guid.NewGuid():N}.shoppilot");
        try
        {
            DocumentSaver.Save(job, Array.Empty<PersistedToolpath>(), path);

            string oursFile = Directory.GetFiles(Path.Combine(path, "sheets"), "*.json")[0];
            string macFile = Directory.GetFiles(Path.Combine(Fixture("Sign"), "sheets"), "*.json")[0];

            using var ours = JsonDocument.Parse(File.ReadAllText(oursFile));
            using var mac = JsonDocument.Parse(File.ReadAllText(macFile));

            foreach (var macProp in mac.RootElement.EnumerateObject())
            {
                Assert.True(ours.RootElement.TryGetProperty(macProp.Name, out _),
                    $"our sheet omits the Mac key '{macProp.Name}'");
            }
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }

    [Fact]
    public void A_Mac_Document_Reloads_After_We_Rewrite_It()
    {
        // Load the Mac's document, save it with OUR writer, load it again.
        var original = DocumentLoader.Load(Fixture("Sign"));
        Assert.NotNull(original.Job);

        string path = Path.Combine(Path.GetTempPath(), $"vp-rewrite-{Guid.NewGuid():N}.shoppilot");
        try
        {
            DocumentSaver.Save(original.Job!, Array.Empty<PersistedToolpath>(), path);
            var again = DocumentLoader.Load(path);

            Assert.Equal(original.Job!.Name, again.Job!.Name);
            Assert.Equal(original.Job.Sheets.Sum(s => s.Layers.Sum(l => l.Shapes.Count)),
                         again.Job!.Sheets.Sum(s => s.Layers.Sum(l => l.Shapes.Count)));
        }
        finally
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
    }
}
