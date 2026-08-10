using System.Text.Json;
using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Serial;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>SPK-0415 parity: post auto-select from the machine profile.</summary>
public class PostSelectorTests
{
    [Fact]
    public void Grbl_Profile_Selects_Grbl_Post_And_Mm_Modal()
    {
        var profile = new MachineProfile { MachineType = MachineType.Grbl, Units = MachineUnits.Millimeter };
        var (post, units, ext) = PostSelector.ForProfile(profile);
        Assert.Equal(PostProcessorType.Grbl, post);
        Assert.Equal(GCodeUnits.Millimeter, units);
        Assert.Equal("gcode", ext);

        var pp = post == PostProcessorType.Grbl ? GRBLPostProcessor.Grbl(units: units) : GRBLPostProcessor.Universal(units: units);
        var out_ = pp.Process(new[] { "G1 X10" }, null).GcodeString;
        Assert.Contains("G21", out_); // millimeter modal
        Assert.DoesNotContain("N10", out_); // GRBL post has no line numbers
    }

    [Fact]
    public void Universal_Profile_Selects_Universal_Post_And_Inch_Modal()
    {
        var profile = new MachineProfile { MachineType = MachineType.Universal, Units = MachineUnits.Inch };
        var (post, units, ext) = PostSelector.ForProfile(profile);
        Assert.Equal(PostProcessorType.Universal, post);
        Assert.Equal(GCodeUnits.Inch, units);
        Assert.Equal("nc", ext);

        var pp = post == PostProcessorType.Grbl ? GRBLPostProcessor.Grbl(units: units) : GRBLPostProcessor.Universal(units: units);
        var out_ = pp.Process(new[] { "G1 X10" }, null).GcodeString;
        Assert.Contains("G20", out_);   // inch modal
        Assert.Contains("G90", out_);   // absolute positioning still present
        Assert.Contains("Post Processor:", out_); // universal wrapper header
    }

    [Fact]
    public void AutoPost_Reflects_MachineType()
    {
        Assert.Equal("grbl", new MachineProfile { MachineType = MachineType.Grbl }.AutoPostProcessorType);
        Assert.Equal("universal", new MachineProfile { MachineType = MachineType.Universal }.AutoPostProcessorType);
    }

    [Fact]
    public void Legacy_Profile_Json_Defaults_To_Grbl_Mm()
    {
        // A legacy profile without machineType/units keys must deserialize to
        // the defaults (Grbl + Millimeter) — forward compatibility.
        var legacy = "{\"Name\":\"Old\",\"PortName\":\"COM3\",\"MaxX\":12,\"MaxY\":24,\"MaxZ\":4}";
        var profile = JsonSerializer.Deserialize<MachineProfile>(legacy)!;
        Assert.Equal(MachineType.Grbl, profile.MachineType);
        Assert.Equal(MachineUnits.Millimeter, profile.Units);
        Assert.Equal("grbl", profile.AutoPostProcessorType);
    }

    [Fact]
    public void Profile_Round_Trips_Type_And_Units()
    {
        var profile = new MachineProfile { Name = "Rotary", MachineType = MachineType.Universal, Units = MachineUnits.Inch };
        var json = JsonSerializer.Serialize(profile);
        var back = JsonSerializer.Deserialize<MachineProfile>(json)!;
        Assert.Equal(MachineType.Universal, back.MachineType);
        Assert.Equal(MachineUnits.Inch, back.Units);
    }
}
