using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

public class JobSetupTests
{
    [Fact]
    public void Point_Budget_Matches_Aspire()
    {
        Assert.Equal(1_000_000, new JobSetupOptions().PointBudget);
        Assert.Equal(4_000_000, new JobSetupOptions { Resolution = ModelingResolution.High }.PointBudget);
    }

    [Fact]
    public void Apply_To_Sheet_Sets_Size_And_Material()
    {
        var options = new JobSetupOptions
        {
            SheetWidthMm = 2440,
            SheetDepthMm = 1220,
            MaterialThicknessMm = 18,
            MaterialName = "MDF"
        };
        var sheet = new Sheet();
        var material = new Material { Name = "MDF" };
        options.ApplyTo(sheet, material);
        Assert.Equal(2440, sheet.Width);
        Assert.Equal(1220, sheet.Height);
        Assert.Equal(18, sheet.Thickness);
        Assert.Same(material, sheet.Material);
    }

    [Fact]
    public void From_Sheet_Round_Trips()
    {
        var sheet = new Sheet { Width = 600, Height = 400, Thickness = 16 };
        var options = JobSetupOptions.From(sheet);
        Assert.Equal(600, options.SheetWidthMm);
        Assert.Equal(400, options.SheetDepthMm);
        Assert.Equal(16, options.MaterialThicknessMm);
    }

    [Fact]
    public void Datum_Defaults_To_Crosshair()
    {
        var o = new JobSetupOptions();
        Assert.True(o.UseCrosshairDatum);
        Assert.Equal(0, o.DatumOffsetXMm);
    }
}
