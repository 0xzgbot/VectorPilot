using System.Diagnostics;
using VectorPilot.App;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// A 4M-cell heightfield finish must not run on the UI thread.
///
/// CutPanel had ZERO async: BtnCalc_Click looped RecalculateToolpath inline, so a large
/// relief froze the window — no repaint, no E-stop, "not responding". The panel now splits
/// the work into a UI-thread prepare/apply pair with entry.Compute on a worker.
///
/// These tests assert the property that makes that split possible: the compute call is
/// pure, so it is safe to hand to Task.Run.
/// </summary>
public class HeightfieldPerformanceTests
{
    private static readonly StrategyRegistry Reg = new();

    /// <summary>2000x2000 = 4,000,000 cells, the app's "High" modeling resolution.</summary>
    private static HeightfieldData BigField()
    {
        const int n = 2000;
        var heights = new double[n * n];
        for (int i = 0; i < heights.Length; i++)
            heights[i] = 5.0 + 3.0 * Math.Sin(i % n / 40.0) * Math.Cos(i / n / 40.0);

        // Real ctor: (width, height, cellSizeMm, minX, minY, heights). All properties are
        // read-only, so an object initialiser does not compile.
        return new HeightfieldData(n, n, cellSizeMm: 0.2, minX: 0, minY: 0, heights: heights);
    }

    private static List<VectorShape> Region() => new() { VectorShape.Rectangle(0, 0, 400, 400) };

    [Fact]
    public void A_Four_Million_Cell_Field_Is_The_Size_We_Claim()
    {
        var hf = BigField();
        Assert.Equal(4_000_000, hf.Width * hf.Height);
        Assert.Equal(4_000_000, hf.Heights.Length);
    }

    [Fact]
    public async Task The_Finish_Strategy_Runs_On_A_Background_Thread()
    {
        // The exact shape of what BtnCalc_Click now does: compute inside Task.Run.
        var entry = Reg.Find("finish3d")!;
        var hf = BigField();
        var shapes = Region();

        int callerThread = Environment.CurrentManagedThreadId;
        int workerThread = 0;

        var result = await Task.Run(() =>
        {
            workerThread = Environment.CurrentManagedThreadId;
            return entry.Compute(shapes, hf, entry.DefaultsJson);
        });

        Assert.NotEqual(callerThread, workerThread);
        Assert.NotEmpty(result.Gcode);
    }

    [Fact]
    public async Task The_Rough_Strategy_Runs_On_A_Background_Thread()
    {
        var entry = Reg.Find("rough3d")!;
        var hf = BigField();
        var shapes = Region();

        int caller = Environment.CurrentManagedThreadId;
        int worker = 0;

        var result = await Task.Run(() =>
        {
            worker = Environment.CurrentManagedThreadId;
            return entry.Compute(shapes, hf, entry.DefaultsJson);
        });

        Assert.NotEqual(caller, worker);
        Assert.NotEmpty(result.Gcode);
    }

    [Fact]
    public async Task The_Caller_Stays_Responsive_While_A_Big_Field_Computes()
    {
        // Simulates the UI thread: while the worker grinds, the "UI" keeps ticking. If
        // compute were inline, the tick count would be zero.
        var entry = Reg.Find("finish3d")!;
        var hf = BigField();
        var shapes = Region();

        int ticks = 0;
        var compute = Task.Run(() => entry.Compute(shapes, hf, entry.DefaultsJson));

        while (!compute.IsCompleted)
        {
            ticks++;
            await Task.Delay(1);
        }

        await compute;
        Assert.True(ticks > 0, "the caller never got control back — the work was not offloaded");
    }

    [Fact]
    public void Compute_Does_Not_Mutate_Its_Inputs()
    {
        // This is what makes Task.Run safe: no shared-state writes to marshal back.
        var entry = Reg.Find("finish3d")!;
        var hf = BigField();
        var shapes = Region();

        var heightsBefore = (double[])hf.Heights.Clone();
        int pointsBefore = shapes[0].Points.Count;

        entry.Compute(shapes, hf, entry.DefaultsJson);

        Assert.Equal(heightsBefore, hf.Heights);
        Assert.Equal(pointsBefore, shapes[0].Points.Count);
    }

    [Fact]
    public void A_Big_Field_Still_Finishes_In_A_Sane_Time()
    {
        // Not a hard perf gate — a guard against an accidental O(n^2) blowup that would
        // make the background pass useless anyway.
        var entry = Reg.Find("finish3d")!;
        var sw = Stopwatch.StartNew();
        var result = entry.Compute(Region(), BigField(), entry.DefaultsJson);
        sw.Stop();

        Assert.NotEmpty(result.Gcode);
        Assert.True(sw.Elapsed.TotalSeconds < 120,
            $"4M-cell finish took {sw.Elapsed.TotalSeconds:F1}s");
    }

    [Fact]
    public async Task Several_Toolpaths_Compute_Sequentially_Without_Blocking()
    {
        // BtnCalc_Click awaits each toolpath in turn; none of them may block the caller.
        var finish = Reg.Find("finish3d")!;
        var rough = Reg.Find("rough3d")!;
        var hf = BigField();
        var shapes = Region();

        int caller = Environment.CurrentManagedThreadId;
        var threads = new List<int>();

        foreach (var entry in new[] { rough, finish })
        {
            await Task.Run(() =>
            {
                threads.Add(Environment.CurrentManagedThreadId);
                return entry.Compute(shapes, hf, entry.DefaultsJson);
            });
        }

        Assert.Equal(2, threads.Count);
        Assert.All(threads, t => Assert.NotEqual(caller, t));
    }
}
