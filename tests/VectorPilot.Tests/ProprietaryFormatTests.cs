using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class ProprietaryFormatTests
{
    [Fact]
    public void V3M_Reports_Not_Implemented()
    {
        Assert.StartsWith("not-implemented", ProprietaryFormatStatus.V3m);
        Assert.False(ProprietaryFormatStatus.IsImplemented(ProprietaryFormatStatus.V3m));
    }

    [Fact]
    public void SKP_Requires_SDK()
    {
        Assert.StartsWith("not-implemented", ProprietaryFormatStatus.Skp);
        Assert.Contains("SketchUpAPI", ProprietaryFormatStatus.Skp);
    }

    [Fact]
    public void ThreeDM_Is_Tracked_Pending()
    {
        Assert.StartsWith("pending", ProprietaryFormatStatus.ThreeDm);
    }

    [Fact]
    public void Unsupported_Registry_Lists_All_Three()
    {
        Assert.Equal(3, ImportCapabilities.Unsupported.Count);
        Assert.Contains(ImportCapabilities.Unsupported, e => e.Format == "V3M 3D Clipart");
    }
}
