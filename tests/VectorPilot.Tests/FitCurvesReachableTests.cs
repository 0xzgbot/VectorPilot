using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Fit curves — the engine DesignPanel.DoFitCurves calls.
///
/// FitCurvesEngine had NO VectorPilot.App call-site, so imported DXF or traced bitmap
/// geometry could never be smoothed before cutting: the machine chewed through every
/// digitiser wobble.
/// </summary>
public class FitCurvesReachableTests
{
    /// <summary>A straight run with deliberate ±0.3mm jitter — 120 points for a 3-point line.</summary>
    private static VectorShape NoisyLine()
    {
        var rng = new Random(1234);
        var pts = new List<VectorPoint>();
        for (int i = 0; i <= 120; i++)
        {
            double x = i * 2.0;
            double y = 50 + (rng.NextDouble() - 0.5) * 0.6;
            pts.Add(new VectorPoint(x, y));
        }
        return VectorShape.Polyline(pts, closed: false);
    }

    /// <summary>An L with one genuinely sharp 90-degree corner, densely sampled.</summary>
    private static VectorShape NoisyCorner()
    {
        var pts = new List<VectorPoint>();
        for (int i = 0; i <= 60; i++) pts.Add(new VectorPoint(i, 0));
        for (int i = 1; i <= 60; i++) pts.Add(new VectorPoint(60, i));
        return VectorShape.Polyline(pts, closed: false);
    }

    /// <summary>
    /// Exactly what DesignPanel.DoFitCurves passes. SimplifyToleranceMm defaults to 0 in
    /// the engine (FitCurvesEngineTests pins bit-exact straight-line pass-through), so the
    /// button opts in and these tests must too.
    /// </summary>
    private static FitCurvesParams P(double smoothing = 0.5)
        => new() { Smoothing = smoothing, SimplifyToleranceMm = 0.05 };

    // ---- point count drops ----

    [Fact]
    public void A_Noisy_Polyline_Loses_Points()
    {
        var shape = NoisyLine();
        int before = shape.Points.Count;

        var r = FitCurvesEngine.Fit(shape, P());

        Assert.Equal(before, r.InputPointCount);
        Assert.True(r.OutputPointCount < before,
            $"fit kept {r.OutputPointCount} of {before} points — nothing was simplified");
    }

    [Fact]
    public void The_Result_Reports_Its_Own_Counts_Consistently()
    {
        var r = FitCurvesEngine.Fit(NoisyLine(), P());

        Assert.Equal(r.OutputPointCount, r.Fitted.Count);
        Assert.True(r.InputPointCount > 0);
    }

    [Fact]
    public void More_Smoothing_Removes_At_Least_As_Many_Points()
    {
        int light = FitCurvesEngine.Fit(NoisyLine(), P(0.1)).OutputPointCount;
        int heavy = FitCurvesEngine.Fit(NoisyLine(), P(1.0)).OutputPointCount;

        Assert.True(heavy <= light,
            $"smoothing 1.0 kept {heavy} points vs {light} at 0.1");
    }

    // ---- endpoints preserved ----

    [Fact]
    public void The_Endpoints_Are_Preserved()
    {
        var shape = NoisyLine();
        var first = shape.Points[0];
        var last = shape.Points[^1];

        var r = FitCurvesEngine.Fit(shape, P());

        Assert.Equal(first.X, r.Fitted[0].X, 3);
        Assert.Equal(first.Y, r.Fitted[0].Y, 3);
        Assert.Equal(last.X, r.Fitted[^1].X, 3);
        Assert.Equal(last.Y, r.Fitted[^1].Y, 3);
    }

    [Fact]
    public void Endpoints_Survive_Maximum_Smoothing()
    {
        var shape = NoisyLine();
        var first = shape.Points[0];
        var last = shape.Points[^1];

        var r = FitCurvesEngine.Fit(shape, P(1.0));

        Assert.Equal(first.X, r.Fitted[0].X, 3);
        Assert.Equal(last.X, r.Fitted[^1].X, 3);
    }

    // ---- geometry is not destroyed ----

    [Fact]
    public void The_Fitted_Path_Stays_Within_The_Original_Bounds()
    {
        var shape = NoisyLine();
        double minX = shape.Points.Min(p => p.X), maxX = shape.Points.Max(p => p.X);
        double minY = shape.Points.Min(p => p.Y), maxY = shape.Points.Max(p => p.Y);

        var r = FitCurvesEngine.Fit(shape, P());

        Assert.All(r.Fitted, p =>
        {
            Assert.InRange(p.X, minX - 1.0, maxX + 1.0);
            Assert.InRange(p.Y, minY - 1.0, maxY + 1.0);
        });
    }

    [Fact]
    public void A_Sharp_Corner_Is_Kept_As_A_Corner()
    {
        // The whole point of "fit curves, preserve corners": the L must not become a bend.
        var r = FitCurvesEngine.Fit(NoisyCorner(), P());

        bool nearCorner = r.Fitted.Any(p =>
            Math.Abs(p.X - 60) < 1.5 && Math.Abs(p.Y - 0) < 1.5);

        Assert.True(nearCorner, "the 90-degree corner at (60,0) was smoothed away");
    }

    [Fact]
    public void Corners_Are_Counted()
    {
        var r = FitCurvesEngine.Fit(NoisyCorner(), P());
        Assert.True(r.CornerCount >= 1, $"reported {r.CornerCount} corners on an L-shape");
    }

    [Fact]
    public void No_NaN_Is_Produced()
    {
        foreach (var p in FitCurvesEngine.Fit(NoisyLine(), P()).Fitted)
            Assert.False(double.IsNaN(p.X) || double.IsNaN(p.Y), "fit produced NaN");
    }

    // ---- degenerate input ----

    [Fact]
    public void A_Two_Point_Line_Is_Returned_Intact()
    {
        var line = VectorShape.Polyline(new List<VectorPoint> { new(0, 0), new(100, 0) }, closed: false);
        var r = FitCurvesEngine.Fit(line, P());

        Assert.True(r.Fitted.Count >= 2);
        Assert.Equal(0.0, r.Fitted[0].X, 3);
        Assert.Equal(100.0, r.Fitted[^1].X, 3);
    }

    [Fact]
    public void Zero_Smoothing_Still_Decimates()
    {
        // Smoothing=0 means "do not move points". It does not mean "keep every point":
        // decimation is controlled separately by SimplifyToleranceMm.
        var shape = NoisyLine();
        var r = FitCurvesEngine.Fit(shape, new FitCurvesParams { Smoothing = 0.0, SimplifyToleranceMm = 0.05 });

        Assert.True(r.Fitted.Count <= shape.Points.Count);
        Assert.Equal(shape.Points[0].X, r.Fitted[0].X, 3);
        Assert.Equal(shape.Points[^1].X, r.Fitted[^1].X, 3);
    }

    [Fact]
    public void Decimation_Can_Be_Turned_Off()
    {
        var shape = NoisyLine();
        var r = FitCurvesEngine.Fit(shape,
            new FitCurvesParams { Smoothing = 0.0, SimplifyToleranceMm = 0 });

        Assert.Equal(shape.Points.Count, r.Fitted.Count);
    }

    [Fact]
    public void A_Looser_Tolerance_Removes_More_Points()
    {
        int Fine(double tol) => FitCurvesEngine.Fit(NoisyLine(),
            new FitCurvesParams { Smoothing = 0.5, SimplifyToleranceMm = tol }).OutputPointCount;

        Assert.True(Fine(1.0) <= Fine(0.01));
    }

    // ---- undo restores the original points ----

    [Fact]
    public void Undo_Restores_The_Original_Point_Count()
    {
        var job = new Job { Name = "fit" };
        var layer = job.ActiveSheet.ActiveLayer;
        var shape = NoisyLine();
        layer.AddShape(shape);

        int before = shape.Points.Count;

        var undo = new UndoStack();
        var snapshot = UndoStack.Snapshot(layer);

        // What DoFitCurves does after snapshotting.
        var r = FitCurvesEngine.Fit(shape, P());
        shape.Points.Clear();
        shape.Points.AddRange(r.Fitted);
        undo.Push("Fit curves", layer, snapshot);

        Assert.True(layer.Shapes[0].Points.Count < before, "nothing was simplified");

        Assert.Equal("Fit curves", undo.Undo());
        Assert.Equal(before, layer.Shapes[0].Points.Count);
    }
}
