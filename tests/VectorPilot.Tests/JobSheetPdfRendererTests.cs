using System.Globalization;
using System.Text;
using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class JobSheetPdfRendererTests
{
    private static JobSheetData Data() => new()
    {
        JobName = "Cabinet Door",
        Material = "MDF",
        SheetWidth = 1220,
        SheetHeight = 610,
        CreatedAt = new DateTime(2026, 8, 10, 9, 30, 0),
        Notes = "Use tabs",
        Toolpaths =
        {
            new ToolpathInfo { Name = "Outline", Type = JobSheetToolpathType.Profile, Tool = "1/4in EM", FeedRate = 1000, Depth = 12.5, EstimatedTime = 240 },
            new ToolpathInfo { Name = "Holes", Type = JobSheetToolpathType.Drill, Tool = "1/8in drill", FeedRate = 800, Depth = 3, EstimatedTime = 60 }
        }
    };

    [Fact]
    public void Output_Is_Well_Formed_Pdf()
    {
        var pdf = JobSheetPdfRenderer.RenderPdf(Data());

        Assert.True(pdf.Length > 100, "PDF should have meaningful size");
        Assert.StartsWith("%PDF-1.4", Encoding.ASCII.GetString(pdf, 0, 8));
        Assert.EndsWith("%%EOF", Encoding.ASCII.GetString(pdf));
    }

    [Fact]
    public void Content_Stream_Contains_Job_Data()
    {
        var pdf = JobSheetPdfRenderer.RenderPdf(Data());
        string s = Encoding.ASCII.GetString(pdf);

        Assert.Contains("Cabinet Door", s);          // title line
        Assert.Contains("MDF", s);                    // material meta line
        Assert.Contains("Outline", s);                // toolpath row 1
        Assert.Contains("Holes", s);                  // toolpath row 2
        Assert.Contains("12.50", s);                  // depth 2dp cell
        Assert.Contains("4.0", s);                    // time in minutes cell
        Assert.Contains("Use tabs", s);               // notes box
        Assert.Contains("MediaBox [0 0 595 842]", s); // A4 portrait page
    }

    [Fact]
    public void Startxref_Points_At_Xref_Keyword()
    {
        var pdf = JobSheetPdfRenderer.RenderPdf(Data());
        long offset = ParseStartxref(pdf);

        Assert.True(offset >= 0 && offset + 4 <= pdf.Length);
        Assert.Equal("xref", Encoding.ASCII.GetString(pdf, (int)offset, 4));
    }

    [Fact]
    public void Xref_Entries_Point_At_Object_Headers()
    {
        var pdf = JobSheetPdfRenderer.RenderPdf(Data());
        string s = Encoding.ASCII.GetString(pdf);

        int xrefIdx = s.IndexOf("xref\n", StringComparison.Ordinal);
        Assert.True(xrefIdx >= 0, "xref table must exist");

        // After "xref\n0 N\n" come N fixed 20-byte entries.
        int countStart = xrefIdx + "xref\n".Length;
        int countEnd = s.IndexOf('\n', countStart);
        Assert.True(countEnd > countStart);
        string countLine = s.Substring(countStart, countEnd - countStart);
        var parts = countLine.Split(' ');
        Assert.Equal("0", parts[0]);
        int count = int.Parse(parts[1], CultureInfo.InvariantCulture);
        Assert.True(count >= 2);

        for (int i = 1; i < count; i++)
        {
            string entry = s.Substring(countEnd + 1 + (i * 20), 20);
            long objOffset = long.Parse(entry.Substring(0, 10), CultureInfo.InvariantCulture);
            string header = $"{i} 0 obj";
            Assert.True(objOffset >= 0 && objOffset + header.Length <= pdf.Length,
                $"object {i} offset {objOffset} out of range");
            Assert.Equal(header, s.Substring((int)objOffset, header.Length));
        }
    }

    private static long ParseStartxref(byte[] pdf)
    {
        string s = Encoding.ASCII.GetString(pdf);
        int idx = s.LastIndexOf("startxref", StringComparison.Ordinal);
        Assert.True(idx >= 0, "startxref keyword must exist");
        int i = idx + "startxref".Length;
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        var digits = new StringBuilder();
        while (i < s.Length && char.IsDigit(s[i])) { digits.Append(s[i]); i++; }
        Assert.True(digits.Length > 0, "startxref must have a numeric offset");
        return long.Parse(digits.ToString(), CultureInfo.InvariantCulture);
    }
}
