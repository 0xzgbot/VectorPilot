using System.IO;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Choosing a post must change the exported program.
///
/// The Output stage shipped a 20-entry post picker, but Export .tap called
/// TapExporter.Export and ignored CmbPost entirely — so the selection had no effect
/// on the file, and it overwrote the same filename the template export wrote. Two
/// buttons, same target, different content.
/// </summary>
public class PostSelectionExportTests
{
    private static List<Toolpath> Toolpaths()
    {
        var tp = new Toolpath { Name = "Profile 1", Strategy = ToolpathStrategy.Profile };
        tp.GCode.AddRange(new[]
        {
            "G0 X0 Y0 Z5",
            "G1 Z-2 F300",
            "G1 X50 Y0 F1000",
            "G1 X50 Y30 F1000",
            "G0 Z5"
        });
        return new List<Toolpath> { tp };
    }

    private static PostTemplate Post(string name)
        => PostTemplate.Shipped.First(p => p.Name.Contains(name, StringComparison.OrdinalIgnoreCase));

    private static string ExportWith(PostTemplate post)
    {
        string path = Path.Combine(Path.GetTempPath(), $"vp-post-{Guid.NewGuid():N}.tap");
        try
        {
            TapExporter.ExportWithTemplate(path, Toolpaths(), post);
            return File.ReadAllText(path);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void The_Catalog_Offers_More_Than_One_Post()
    {
        Assert.True(PostTemplate.Shipped.Count >= 2, "need at least two posts to differ");
    }

    [Fact]
    public void Two_Different_Posts_Produce_Two_Different_Programs()
    {
        // The core requirement: same toolpath, different post, different .tap.
        var a = ExportWith(PostTemplate.Shipped[0]);
        var b = ExportWith(PostTemplate.Shipped.First(p => p.Name != PostTemplate.Shipped[0].Name));

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Metric_And_Imperial_Posts_Emit_Different_Unit_Codes()
    {
        var mm = ExportWith(Post("mm"));
        var inch = ExportWith(Post("inch"));

        Assert.NotEqual(mm, inch);
        // G21 = mm, G20 = inches. Whichever way round, they must not match.
        bool mmHasG21 = mm.Contains("G21");
        bool inchHasG20 = inch.Contains("G20");
        Assert.True(mmHasG21 || inchHasG20, "neither post declared its unit mode");
    }

    [Fact]
    public void Every_Shipped_Post_Exports_Without_Throwing()
    {
        foreach (var post in PostTemplate.Shipped)
        {
            var text = ExportWith(post);
            Assert.False(string.IsNullOrWhiteSpace(text), $"{post.Name} produced nothing");
        }
    }

    [Fact]
    public void Every_Shipped_Post_Carries_The_Cutting_Moves()
    {
        // A post changes the wrapper, never the geometry.
        foreach (var post in PostTemplate.Shipped)
        {
            var text = ExportWith(post);
            Assert.Contains("X50", text);
        }
    }

    [Fact]
    public void Posts_Have_Distinct_Names()
    {
        var dupes = PostTemplate.Shipped.GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dupes.Count == 0, "duplicate post names: " + string.Join(", ", dupes));
    }

    [Fact]
    public void The_Export_Writes_A_Non_Empty_File()
    {
        string path = Path.Combine(Path.GetTempPath(), $"vp-post-{Guid.NewGuid():N}.tap");
        try
        {
            var written = TapExporter.ExportWithTemplate(path, Toolpaths(), PostTemplate.Shipped[0]);
            Assert.True(File.Exists(written));
            Assert.True(new FileInfo(written).Length > 20);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void A_Post_Changes_The_Header_Or_Footer_Not_Just_Whitespace()
    {
        var a = ExportWith(PostTemplate.Shipped[0]).Split('\n').Select(l => l.Trim()).ToList();
        var b = ExportWith(PostTemplate.Shipped[1]).Split('\n').Select(l => l.Trim()).ToList();

        // Ignore blank lines; there must be a real line-level difference.
        var onlyInA = a.Except(b).Where(l => l.Length > 0).ToList();
        var onlyInB = b.Except(a).Where(l => l.Length > 0).ToList();

        Assert.True(onlyInA.Count > 0 || onlyInB.Count > 0,
            "posts differ only in whitespace — the selection is not reaching the file");
    }
}
