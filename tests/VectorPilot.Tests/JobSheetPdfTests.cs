using System.IO;
using System.Text;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// The Export PDF button must write a real file.
///
/// OutputPanel used to probe for JobSheetPdfRenderer by reflection and show
/// "PDF job-sheet renderer is not available yet" when the probe failed — while the
/// renderer shipped in VectorPilot.Engine the whole time. These tests pin the
/// renderer's contract so the direct call cannot regress into that again.
/// </summary>
public class JobSheetPdfTests
{
    private static JobSheetData Sample() => new()
    {
        JobName = "Bracket v2",
        Material = "Baltic birch ply",
        SheetWidth = 600,
        SheetHeight = 400,
        Toolpaths = new List<ToolpathInfo>
        {
            new() { Name = "Profile 1", Tool = "6mm flat", Depth = 18, FeedRate = 1000, EstimatedTime = 412 },
            new() { Name = "Pocket 1", Tool = "6mm flat", Depth = 6, FeedRate = 1200, EstimatedTime = 260 }
        }
    };

    [Fact]
    public void Renders_A_Non_Empty_Document()
    {
        var pdf = JobSheetPdfRenderer.RenderPdf(Sample());
        Assert.NotNull(pdf);
        Assert.True(pdf.Length > 200, $"PDF was only {pdf.Length} bytes");
    }

    [Fact]
    public void Starts_With_The_Pdf_Magic_Header()
    {
        var pdf = JobSheetPdfRenderer.RenderPdf(Sample());
        var header = Encoding.ASCII.GetString(pdf, 0, 5);
        Assert.Equal("%PDF-", header);
    }

    [Fact]
    public void Ends_With_The_Eof_Marker()
    {
        var pdf = JobSheetPdfRenderer.RenderPdf(Sample());
        var tail = Encoding.ASCII.GetString(pdf, Math.Max(0, pdf.Length - 32), Math.Min(32, pdf.Length));
        Assert.Contains("%%EOF", tail);
    }

    [Fact]
    public void Declares_A_Page_And_A_Catalog()
    {
        var text = Encoding.Latin1.GetString(JobSheetPdfRenderer.RenderPdf(Sample()));
        Assert.Contains("/Catalog", text);
        Assert.Contains("/Page", text);
    }

    [Fact]
    public void Carries_The_Job_Identity()
    {
        var text = Encoding.Latin1.GetString(JobSheetPdfRenderer.RenderPdf(Sample()));
        Assert.Contains("Bracket v2", text);
        Assert.Contains("Baltic birch ply", text);
    }

    [Fact]
    public void Lists_Every_Toolpath_Row()
    {
        var text = Encoding.Latin1.GetString(JobSheetPdfRenderer.RenderPdf(Sample()));
        Assert.Contains("Profile 1", text);
        Assert.Contains("Pocket 1", text);
    }

    [Fact]
    public void An_Empty_Job_Still_Produces_A_Valid_Pdf()
    {
        var pdf = JobSheetPdfRenderer.RenderPdf(new JobSheetData { JobName = "Empty" });
        Assert.True(pdf.Length > 200);
        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
    }

    [Fact]
    public void Writes_A_Non_Empty_File_To_Disk()
    {
        // The button's actual effect: bytes on disk that a viewer can open.
        string path = Path.Combine(Path.GetTempPath(), $"vp-jobsheet-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllBytes(path, JobSheetPdfRenderer.RenderPdf(Sample()));

            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 200);

            using var fs = File.OpenRead(path);
            var head = new byte[5];
            Assert.Equal(5, fs.Read(head, 0, 5));
            Assert.Equal("%PDF-", Encoding.ASCII.GetString(head));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Special_Characters_In_The_Job_Name_Do_Not_Corrupt_The_Pdf()
    {
        var pdf = JobSheetPdfRenderer.RenderPdf(new JobSheetData
        {
            JobName = @"Sign (v3) \ 50% — ""quoted""",
            Material = "Oak"
        });

        Assert.Equal("%PDF-", Encoding.ASCII.GetString(pdf, 0, 5));
        var tail = Encoding.ASCII.GetString(pdf, Math.Max(0, pdf.Length - 32), Math.Min(32, pdf.Length));
        Assert.Contains("%%EOF", tail);
    }
}
