using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class JobSheetHTMLTemplateTests
{
    private static JobSheetData Data() => new()
    {
        JobName = "Cabinet Door",
        Material = "MDF",
        SheetWidth = 1220,
        SheetHeight = 610,
        Notes = "Use tabs",
        Toolpaths =
        {
            new ToolpathInfo { Name = "Outline", Type = JobSheetToolpathType.Profile, Tool = "1/4in EM", FeedRate = 1000, Depth = 12.5, EstimatedTime = 240 },
            new ToolpathInfo { Name = "Holes", Type = JobSheetToolpathType.Drill, Tool = "1/8in drill", FeedRate = 800, Depth = 3, EstimatedTime = 60 }
        }
    };

    [Fact]
    public void Fill_Substitutes_All_Tokens()
    {
        var html = JobSheetHTMLTemplateEngine.Fill(Data());
        Assert.Contains("<h1>Cabinet Door</h1>", html);
        Assert.Contains("MDF", html);
        Assert.Contains("1220.0 × 610.0 mm", html);
        Assert.Contains(">2<", html); // toolpath count
        Assert.Contains("Outline", html);
    }

    [Fact]
    public void Toolpath_Rows_Format_Values()
    {
        var rows = JobSheetHTMLTemplateEngine.ToolpathRows(Data().Toolpaths);
        Assert.Contains("1/4in EM", rows);
        Assert.Contains(">12.50<", rows); // depth 2dp
        Assert.Contains(">4.0<", rows);   // time in minutes
    }

    [Fact]
    public void Escape_Prevents_Injections()
    {
        var data = new JobSheetData
        {
            JobName = "<script>alert(1)</script>",
            Notes = "x",
            Toolpaths = { new ToolpathInfo { Name = "A&B", Type = JobSheetToolpathType.Profile, Tool = "t", FeedRate = 1, Depth = 1, EstimatedTime = 1 } }
        };
        var html = JobSheetHTMLTemplateEngine.Fill(data);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("A&amp;B", html);
    }
}
