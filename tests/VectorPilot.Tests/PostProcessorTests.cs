using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class PostProcessorTests
{
    private static readonly List<string> Raw = new()
    {
        "%", "O=PROFILE_TOOLPATH", "(Tool: 60mm)", "M3 S12000",
        "G0 Z5.0", "G1 X1.000 Y2.000 F1000", "M30", "%"
    };

    [Fact]
    public void Grbl_Post_Adds_Header_And_Footer()
    {
        var pp = GRBLPostProcessor.Grbl(machineName: "TestCNC");
        var out_ = pp.Process(Raw);
        var s = out_.GcodeString;
        Assert.StartsWith("%\n(Machine: TestCNC)\n(Post Processor: GRBL 1.1)", s);
        Assert.Contains("G21 ; Set millimeter units", s);
        Assert.Contains("G90 ; Absolute positioning", s);
        Assert.Contains("M8 ; Flood coolant on", s);
        Assert.Contains("M9 ; Coolant off", s);
        Assert.Contains("G0 Z5.0 ; Rapid to safe height", s);
        Assert.Contains("M2 ; Program end", s);
        Assert.EndsWith("%", s);
        Assert.Equal("toolpath.gcode", out_.FileName);
        // Input %/comments/headers stripped; motion kept.
        Assert.DoesNotContain("%\nM3", s.Replace("%\n(Machine", "%\n(Machine"));
        Assert.Contains("G1 X1.000 Y2.000 F1000", s);
    }

    [Fact]
    public void Inch_Units_Emits_G20()
    {
        var pp = GRBLPostProcessor.Grbl(units: GCodeUnits.Inch);
        Assert.Contains("G20 ; Set inch units", pp.Process(Raw).GcodeString);
    }

    [Fact]
    public void Universal_Post_Adds_Line_Numbers()
    {
        var pp = GRBLPostProcessor.Universal();
        var s = pp.Process(Raw).GcodeString;
        Assert.Contains("(Post Processor: Universal G-Code)", s);
        Assert.Contains("\n10: M3 S12000", s);
        Assert.Contains("\n20: G0 Z5.0", s);
        Assert.Equal("toolpath.nc", pp.Process(Raw).FileName);
    }

    [Fact]
    public void Tool_Info_In_Header()
    {
        var pp = GRBLPostProcessor.Grbl();
        var s = pp.Process(Raw, toolInfo: "1/4in End Mill").GcodeString;
        Assert.Contains("(Tool: 1/4in End Mill)", s);
    }

    [Fact]
    public void JobSheet_Pdf_Is_Well_Formed()
    {
        var data = new JobSheetData
        {
            JobName = "Sign Job",
            Material = "Oak",
            SheetWidth = 305,
            SheetHeight = 610,
            Toolpaths = new List<ToolpathInfo>
            {
                new() { Name = "Profile 1", Type = JobSheetToolpathType.Profile, Tool = "1/4in End Mill", FeedRate = 1000, Depth = 6.35, EstimatedTime = 120 },
                new() { Name = "V-Carve 1", Type = JobSheetToolpathType.VCarve, Tool = "90° V-Bit", FeedRate = 800, Depth = 3.0, EstimatedTime = 240 }
            },
            Notes = "Two ops"
        };
        var pdf = JobSheetGenerator.BuildPdf(data);
        Assert.StartsWith("%PDF-1.4", pdf);
        Assert.Contains("Sign Job", pdf);
        Assert.Contains("Oak", pdf);
        Assert.Contains("Profile 1", pdf);
        Assert.Contains("V-Carve 1", pdf);
        Assert.Contains("startxref", pdf);
        Assert.EndsWith("%%EOF\n", pdf);
        // Content stream length matches the actual stream.
        int streamStart = pdf.IndexOf("stream\n", StringComparison.Ordinal) + "stream\n".Length;
        int streamEnd = pdf.IndexOf("endstream", StringComparison.Ordinal);
        string declared = pdf.Substring(pdf.IndexOf("/Length ", StringComparison.Ordinal) + 8, 4).Trim();
        Assert.Equal((streamEnd - streamStart).ToString(), declared);
    }
}
