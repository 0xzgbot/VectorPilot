using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>SPK-0211/0212 parity: the Vector Preflight Doctor.</summary>
public class VectorPreflightDoctorTests
{
    [Fact]
    public void Open_Line_And_Open_Path_Are_Flagged_With_Index()
    {
        var shapes = new List<VectorShape>
        {
            VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(10, 0)),   // open
            VectorShape.Rectangle(0, 0, 10, 10),                                // closed
            VectorShape.Polyline(new[] { new VectorPoint(0, 0), new VectorPoint(5, 5), new VectorPoint(10, 0) }, closed: false) // open
        };
        var issues = VectorPreflightDoctor.Check(shapes).Where(i => i.Kind == VectorDoctorKind.OpenPath).ToList();
        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, i => i.ShapeIndices.Contains(0) && i.Severity == VectorDoctorSeverity.Error);
        Assert.Contains(issues, i => i.ShapeIndices.Contains(2));
    }

    [Fact]
    public void Bowtie_Is_Self_Intersecting_Closed_Square_Is_Not()
    {
        var bowtie = new VectorShape { Type = ShapeType.Polyline, Closed = true };
        bowtie.Points.AddRange(new[] { new VectorPoint(0, 0), new VectorPoint(10, 10), new VectorPoint(10, 0), new VectorPoint(0, 10) });
        var square = VectorShape.Rectangle(0, 0, 10, 10);

        var issues = VectorPreflightDoctor.Check(new[] { bowtie });
        Assert.Contains(issues, i => i.Kind == VectorDoctorKind.SelfIntersection && i.Severity == VectorDoctorSeverity.Warning);

        Assert.Empty(VectorPreflightDoctor.Check(new[] { square }).Where(i => i.Kind == VectorDoctorKind.SelfIntersection));
    }

    [Fact]
    public void Zero_Length_Line_And_Zero_Radius_Circle_Are_Degenerate()
    {
        var zeroLine = VectorShape.Line(new VectorPoint(5, 5), new VectorPoint(5, 5));
        var zeroCircle = new VectorShape { Type = ShapeType.Circle, Closed = true, Radius = 0 };
        zeroCircle.Points.Add(new VectorPoint(0, 0));

        var issues = VectorPreflightDoctor.Check(new[] { zeroLine, zeroCircle });
        var degen = issues.Where(i => i.Kind == VectorDoctorKind.Degenerate).ToList();
        Assert.Equal(2, degen.Count);
        Assert.All(degen, i => Assert.Equal(VectorDoctorSeverity.Warning, i.Severity));
    }

    [Fact]
    public void Near_Shapes_Flag_Gap_Far_And_Touching_Do_Not()
    {
        // Gap: rect 0..10, second rect starting at x=10.5 → 0.5mm gap.
        var a = VectorShape.Rectangle(0, 0, 10, 10);
        var near = VectorShape.Rectangle(10.5, 0, 20, 10);
        var far = VectorShape.Rectangle(50, 50, 60, 60);
        var touching = VectorShape.Rectangle(10, 0, 20, 10);

        var nearIssues = VectorPreflightDoctor.Check(new[] { a, near }).Where(i => i.Kind == VectorDoctorKind.Gap).ToList();
        Assert.Single(nearIssues);
        Assert.Equal(VectorDoctorSeverity.Info, nearIssues[0].Severity);
        Assert.Contains(0, nearIssues[0].ShapeIndices);
        Assert.Contains(1, nearIssues[0].ShapeIndices);

        Assert.DoesNotContain(VectorPreflightDoctor.Check(new[] { a, far }), i => i.Kind == VectorDoctorKind.Gap);
        Assert.DoesNotContain(VectorPreflightDoctor.Check(new[] { a, touching }), i => i.Kind == VectorDoctorKind.Gap);
    }

    [Fact]
    public void Clean_Closed_Rect_Plus_Far_Circle_Produce_No_Issues()
    {
        var shapes = new[] { VectorShape.Rectangle(0, 0, 10, 10), VectorShape.Circle(new VectorPoint(100, 100), 5) };
        Assert.Empty(VectorPreflightDoctor.Check(shapes));
    }

    [Fact]
    public void Fix_Actions_Carry_Plain_English_Titles()
    {
        var shapes = new List<VectorShape> { VectorShape.Line(new VectorPoint(0, 0), new VectorPoint(10, 0)) };
        var issue = VectorPreflightDoctor.Check(shapes).Single();
        Assert.Equal("Open vector", issue.Title);
        Assert.Equal(VectorDoctorSeverity.Error, issue.Severity);
        Assert.Equal(new[] { 0 }, issue.ShapeIndices);
    }
}

/// <summary>SPK-0412a parity: the preflight checklist gates the run on
/// required spindle + work-zero items, which can never be bypassed.</summary>
public class PreflightChecklistTests
{
    [Fact]
    public void Run_Is_Gated_Until_Required_Items_Acknowledged()
    {
        var checklist = PreflightChecklist.CreateDefault();
        Assert.False(checklist.IsComplete); // spindle + work-zero + material unacknowledged

        checklist.Spindle.Acknowledge();
        Assert.False(checklist.IsComplete);
        checklist.WorkZero.Acknowledge();
        Assert.False(checklist.IsComplete); // material also required
        checklist.Items.First(i => i.Id == "material").Acknowledge();
        Assert.True(checklist.IsComplete);
    }

    [Fact]
    public void Required_Items_Can_Never_Be_Bypassed()
    {
        var checklist = PreflightChecklist.CreateDefault();
        Assert.False(checklist.CanBypass(checklist.Spindle));
        Assert.False(checklist.CanBypass(checklist.WorkZero));
        Assert.True(checklist.CanBypass(checklist.Items.First(i => i.Id == "dust")));
    }

    [Fact]
    public void Missing_Required_Lists_Only_Unacknowledged_Required()
    {
        var checklist = PreflightChecklist.CreateDefault();
        checklist.Spindle.Acknowledge();
        var missing = checklist.MissingRequired();
        Assert.Equal(2, missing.Count);
        Assert.Contains(missing, m => m.Contains("work zero", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(missing, m => m.Contains("dust"));
    }
}
