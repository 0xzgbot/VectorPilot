using VectorPilot.Engine;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// H-502: flash recipes. Each recipe creates a job with at least one toolpath
/// ready to Calculate — the AC is "ready to Calculate", so the tests pin real
/// geometry/program content, not just non-null objects.
/// </summary>
public class FlashRecipeTests
{
    [Fact]
    public void Photo_Plaque_Recipe_Builds_A_Complete_Job()
    {
        var job = FlashRecipeManager.CreatePhotoPlaqueJob();

        Assert.Equal("Photo Plaque", job.Name);
        Assert.NotEmpty(job.Sheets);

        var sheet = job.Sheets[0];
        Assert.Equal(200, sheet.Width, 3);
        Assert.Equal(150, sheet.Height, 3);

        // The plaque face outline exists and is a closed carve-able shape.
        var face = sheet.Layers.FirstOrDefault(l => l.Name == "Plaque Face");
        Assert.NotNull(face);
        var oval = face!.Shapes.Single();
        Assert.True(oval.Closed);
        Assert.True(oval.Points.Count >= 12, "outline too coarse to carve cleanly");

        // Points must land inside the sheet bounds.
        Assert.All(oval.Points, p =>
        {
            Assert.InRange(p.X, 0, sheet.Width);
            Assert.InRange(p.Y, 0, sheet.Height);
        });
    }

    [Fact]
    public void Coaster_Recipe_Ships_A_Real_Pocket_Program()
    {
        var job = FlashRecipeManager.CreateCoasterJob();

        Assert.Equal("3D Coaster", job.Name);

        var recess = job.Sheets[0].Layers.FirstOrDefault(l => l.Name == "Recess");
        Assert.NotNull(recess);
        var circle = recess!.Shapes.Single();
        Assert.True(circle.Closed);

        // The pocket program is REAL G-code: spindle-on, absolute mode, Z plunges.
        Assert.NotNull(job.VcarveGCode);
        Assert.NotEmpty(job.VcarveGCode!);
        Assert.Contains(job.VcarveGCode, l => l.Contains("M3"));
        Assert.Contains(job.VcarveGCode, l => l.Contains("G90"));
        Assert.Contains(job.VcarveGCode, l => l.StartsWith("G1"));
    }

    [Fact]
    public void Recipes_Are_Size_Parametric()
    {
        var small = FlashRecipeManager.CreateCoasterJob(sizeMm: 60);
        var large = FlashRecipeManager.CreateCoasterJob(sizeMm: 120);

        Assert.Equal(60, small.Sheets[0].Width, 1);
        Assert.Equal(120, large.Sheets[0].Width, 1);
        // Recess radius scales with stock (30% of size).
        var rSmall = small.Sheets[0].Layers.First(l => l.Name == "Recess").Shapes[0].Points.Max(p => p.X);
        var rLarge = large.Sheets[0].Layers.First(l => l.Name == "Recess").Shapes[0].Points.Max(p => p.X);
        Assert.True(rLarge > rSmall * 1.5, $"recess did not scale: {rSmall} vs {rLarge}");
    }
}
