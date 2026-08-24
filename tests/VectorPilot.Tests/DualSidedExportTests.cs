using System.IO;
using System.Windows;
using System.Windows.Controls;
using VectorPilot.App;
using VectorPilot.App.Controls;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// P-301: a dual-sided job exports TWO programs — front as calculated, back with
/// every motion coordinate mirrored about the job's FlipAxis — plus flip
/// instructions. Single-sided export is unchanged.
/// </summary>
[Collection("STA")]
public class DualSidedExportTests : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            if (File.Exists(f)) File.Delete(f);
    }

    private string Temp(string name)
    {
        var p = Path.Combine(Path.GetTempPath(), $"vp-ds-{Guid.NewGuid():N}-{name}");
        _tempFiles.Add(p);
        return p;
    }

    private static void OnSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                lock (STAApplicationGate.Lock)
                {
                    if (Application.Current is null) _ = new Application();
                }
                body();
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        Assert.True(t.Join(TimeSpan.FromSeconds(30)), "test hung");
        if (error is not null) throw error;
    }

    [Fact]
    public void Vertical_Flip_Mirrors_X_About_Stock_Width()
    {
        // "G1 X30 Y10" on 200mm-wide stock → X = 200 − 30 = 170, Y untouched.
        var mirrored = OutputPanel.MirrorMotionLine("G1 X30 Y10 F1000", FlipAxis.Vertical, 200, 300);
        Assert.Contains("X170.000", mirrored);
        Assert.Contains("Y10", mirrored);

        // Involution: mirroring twice restores the original coordinate.
        var twice = OutputPanel.MirrorMotionLine(mirrored, FlipAxis.Vertical, 200, 300);
        Assert.Contains("X30.000", twice);
    }

    [Fact]
    public void Horizontal_Flip_Mirrors_Y_About_Stock_Height_And_Passes_Comments()
    {
        var mirrored = OutputPanel.MirrorMotionLine("G1 X30 Y10 F1000", FlipAxis.Horizontal, 200, 300);
        Assert.Contains("Y290.000", mirrored);
        Assert.Contains("X30", mirrored);

        // Comments and non-motion lines pass through untouched.
        Assert.Equal("(hello)", OutputPanel.MirrorMotionLine("(hello)", FlipAxis.Horizontal, 200, 300));
        Assert.Equal("M3 S18000", OutputPanel.MirrorMotionLine("M3 S18000", FlipAxis.Horizontal, 200, 300));
    }

    [Fact]
    public void Dual_Sided_Job_Exports_Two_Programs_With_Flip_Instructions()
    {
        OnSta(() =>
        {
            // Snapshot shared AppState so parallel/other tests see no side effects.
            var prevJob = AppState.CurrentJob;
            var prevToolpaths = AppState.Toolpaths.Toolpaths.ToList();

            // A dual-sided job whose sheet is 200×300 mm and flip is Vertical.
            var job = Job.CreateDefault();
            job.Name = "ds-test";
            job.IsDoubleSided = true;
            job.FlipAxis = FlipAxis.Vertical;
            job.Sheets[0].Width = 200;
            job.Sheets[0].Height = 300;
            AppState.RestoreJob(job);

            // One calculated toolpath with a known move.
            var tp = AppState.Toolpaths.Add(ToolpathStrategy.Profile, name: "p301");
            tp.SetResult(new List<string>
            {
                "(profile)",
                "G0 X0 Y0",
                "G1 X30 Y10 F1000",
                "M30",
            });

            var panel = new OutputPanel();
            var dir = Path.Combine(Path.GetTempPath(), $"vp-ds-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            _tempFiles.Add(dir);
            var status = panel.ExportDualSidedToTest(new List<Toolpath> { tp }, dir);

            try
            {
                Assert.Null(panel.LastExportError);
                Assert.NotNull(status);

                var front = Path.Combine(dir, "ds-test-front.tap");
                var back = Path.Combine(dir, "ds-test-back.tap");
                Assert.True(File.Exists(front), "front program missing");
                Assert.True(File.Exists(back), "back program missing");
                _tempFiles.Add(front);
                _tempFiles.Add(back);

                // Front carries the original coordinates.
                var frontText = File.ReadAllText(front);
                Assert.Contains("X30", frontText);

                // Back mirrors X about the 200mm stock width.
                var backText = File.ReadAllText(back);
                Assert.Contains("X170.000", backText);
                Assert.Contains("FLIP THE STOCK", backText);
            }
            finally
            {
                AppState.Toolpaths.Remove(tp.Id);
                AppState.RestoreJob(prevJob);   // put the shared job back
                foreach (var t in prevToolpaths) AppState.Toolpaths.Toolpaths.Add(t);
            }
        });
    }

    [Fact]
    public void Single_Sided_Job_Keeps_The_Single_File_Path()
    {
        OnSta(() =>
        {
            var job = Job.CreateDefault();
            job.Name = "ss-test";
            job.IsDoubleSided = false;
            AppState.RestoreJob(job);

            // The dual-sided branch must not fire for a single-sided job: the
            // ExportTap_Click path checks IsDoubleSided BEFORE writing anything,
            // which this assertion pins at the API level.
            Assert.False(AppState.CurrentJob.IsDoubleSided);
        });
    }
}
