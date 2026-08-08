using System.IO;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class TapExporterTests
{
    [Fact]
    public void Export_Writes_Tap_With_Header_And_Post()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"vp-tap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var job = Path.Combine(dir, "test.shoppilot");
            var tp = new Toolpath { Name = "Profile 1", Strategy = ToolpathStrategy.Profile, EstimatedTimeSeconds = 42 };
            tp.GCode.AddRange(new[] { "%", "O=PROFILE_TOOLPATH", "G0 X0 Y0", "M30", "%" });

            var outPath = TapExporter.Export(TapExporter.DefaultPath(job), new[] { tp });
            Assert.EndsWith(".tap", outPath);
            Assert.True(File.Exists(outPath));

            var text = File.ReadAllText(outPath);
            Assert.Contains("(VectorPilot job export)", text);
            Assert.Contains("(Toolpath: Profile 1", text);
            Assert.Contains("(Est. time: 00:00:42)", text);
            Assert.Contains("G21", text); // post processor applied
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}

public class JobSheetHtmlTests
{
    [Fact]
    public void Render_Contains_Job_And_Toolpaths()
    {
        var rows = new List<JobSheetRow>
        {
            new() { Name = "Outline", Strategy = "profile", Tool = "1/4in EM", EstimatedSeconds = 120, LineCount = 55 },
            new() { Name = "Holes", Strategy = "drill", Tool = "1/8in drill", EstimatedSeconds = 30, LineCount = 12 }
        };
        var html = JobSheetHtml.Render("Cabinet Door", @"C:\jobs\door.shoppilot", 1220, 610, 18, "MDF", "mm", rows);

        Assert.Contains("Cabinet Door", html);
        Assert.Contains("1220 × 610", html);
        Assert.Contains("MDF", html);
        Assert.Contains("Toolpaths (2)", html);
        Assert.Contains("Outline", html);
        Assert.Contains("00:02:00", html);
        Assert.Contains("Holes", html);
    }

    [Fact]
    public void Render_Escapes_Html()
    {
        var html = JobSheetHtml.Render("<script>alert(1)</script>", "x", 100, 100, 10, "mat", "mm", new List<JobSheetRow>
        {
            new() { Name = "A&B", Strategy = "p", Tool = "t", EstimatedSeconds = 1, LineCount = 2 }
        });
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("A&amp;B", html);
    }

    [Fact]
    public void RenderToFile_Writes_Html()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vp-sheet-{Guid.NewGuid():N}.html");
        try
        {
            JobSheetHtml.RenderToFile(path, "Job", "p", 100, 100, 10, "mat", "mm", new List<JobSheetRow>());
            Assert.True(File.Exists(path));
            Assert.StartsWith("<!DOCTYPE html>", File.ReadAllText(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
