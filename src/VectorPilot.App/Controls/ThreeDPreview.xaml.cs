using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using VectorPilot.Engine;

namespace VectorPilot.App.Controls;

/// <summary>
/// 3D preview: renders a heightfield as a shaded mesh (Viewport3D, no NuGet)
/// plus a toolpath wireframe overlay. Z-up; X/Y map to stock coordinates.
/// </summary>
public partial class ThreeDPreview : System.Windows.Controls.UserControl
{
    private const double ZScale = 1.0;

    public ThreeDPreview()
    {
        InitializeComponent();
    }

    /// <summary>Show the heightfield as a solid mesh. Pass null to clear.</summary>
    public void ShowHeightfield(HeightfieldData? heightfield, double? maxZ = null)
    {
        StockModel.Content = heightfield is null ? null : BuildMesh(heightfield, maxZ ?? heightfield.MaxHeight);
    }

    /// <summary>Overlay toolpath segments (rapid = dashed red, cut = solid blue).</summary>
    public void ShowToolpath(IEnumerable<WireframeRenderer.Segment>? segments)
    {
        ToolpathModel.Content = segments is null ? null : BuildLines(segments);
    }

    /// <summary>
    /// Ghost-diff overlay (SPK-0316): old toolpath rendered in amber, new toolpath in the
    /// existing blue, both drawn on top of each other. Simple v1 — the index-paired diff
    /// from <see cref="ToolpathDiff.CompareLines"/> is used only to decide whether a ghost
    /// overlay is needed at all; when nothing changed the regular blue path is shown.
    /// </summary>
    public void ShowGhostDiff(IReadOnlyList<string>? oldGcode, IReadOnlyList<string>? newGcode)
    {
        if (oldGcode is null || newGcode is null) return;

        var newSegments = WireframeRenderer.GenerateSegments(newGcode);
        var diff = ToolpathDiff.CompareLines(oldGcode, newGcode);

        bool anyDiff = false;
        foreach (var d in diff)
        {
            if (d.OnlyInOld || d.OnlyInNew)
            {
                anyDiff = true;
                break;
            }
        }
        if (!anyDiff)
        {
            ShowToolpath(newSegments);
            return;
        }

        var oldSegments = WireframeRenderer.GenerateSegments(oldGcode);
        var group = new Model3DGroup
        {
            Children =
            {
                BuildLinesColored(oldSegments, Color.FromRgb(0xC0, 0x80, 0x20)), // amber ghost
                BuildLinesColored(newSegments, Color.FromRgb(0x20, 0x60, 0xC0))  // current blue
            }
        };
        ToolpathModel.Content = group;
    }

    private static Model3DGroup BuildMesh(HeightfieldData hf, double maxZ)
    {
        var mesh = new MeshGeometry3D();
        int w = hf.Width, h = hf.Height;
        double cell = hf.CellSizeMm;

        for (int row = 0; row < h - 1; row++)
        {
            for (int col = 0; col < w - 1; col++)
            {
                double x0 = (col - (w - 1) / 2.0) * cell;
                double x1 = x0 + cell;
                double y0 = (row - (h - 1) / 2.0) * cell;
                double y1 = y0 + cell;
                double z00 = hf.Heights[row * w + col] * ZScale;
                double z10 = hf.Heights[row * w + col + 1] * ZScale;
                double z01 = hf.Heights[(row + 1) * w + col] * ZScale;
                double z11 = hf.Heights[(row + 1) * w + col + 1] * ZScale;

                // Triangle 1 (00, 10, 11)
                mesh.Positions.Add(new Point3D(x0, y0, z00));
                mesh.Positions.Add(new Point3D(x1, y0, z10));
                mesh.Positions.Add(new Point3D(x1, y1, z11));
                // Triangle 2 (00, 11, 01)
                mesh.Positions.Add(new Point3D(x0, y0, z00));
                mesh.Positions.Add(new Point3D(x1, y1, z11));
                mesh.Positions.Add(new Point3D(x0, y1, z01));
            }
        }
        mesh.Freeze();

        var brush = new SolidColorBrush(Color.FromRgb(0xC8, 0x90, 0x50));
        brush.Freeze();
        var material = new DiffuseMaterial(brush);
        material.Freeze();

        return new Model3DGroup
        {
            Children =
            {
                new GeometryModel3D(mesh, material),
                new GeometryModel3D(BuildFloor(hf, maxZ), new DiffuseMaterial(Brushes.Gray))
            }
        };
    }

    private static MeshGeometry3D BuildFloor(HeightfieldData hf, double maxZ)
    {
        var floor = new MeshGeometry3D();
        double w2 = (hf.Width - 1) * hf.CellSizeMm / 2.0;
        double h2 = (hf.Height - 1) * hf.CellSizeMm / 2.0;
        var z = -maxZ * 0.02;
        floor.Positions.Add(new Point3D(-w2, -h2, z));
        floor.Positions.Add(new Point3D(w2, -h2, z));
        floor.Positions.Add(new Point3D(w2, h2, z));
        floor.Positions.Add(new Point3D(-w2, -h2, z));
        floor.Positions.Add(new Point3D(w2, h2, z));
        floor.Positions.Add(new Point3D(-w2, h2, z));
        floor.Freeze();
        return floor;
    }

    private static Model3DGroup BuildLines(IEnumerable<WireframeRenderer.Segment> segments)
    {
        var group = new Model3DGroup();
        foreach (var seg in segments)
        {
            var color = seg.IsRapid ? Color.FromRgb(0xC0, 0x30, 0x30) : Color.FromRgb(0x20, 0x60, 0xC0);
            group.Children.Add(CreateLineModel(seg, color));
        }
        return group;
    }

    /// <summary>Single-color line overlay (ghost diff): every segment drawn in the given color.</summary>
    private static Model3DGroup BuildLinesColored(IEnumerable<WireframeRenderer.Segment> segments, Color color)
    {
        var group = new Model3DGroup();
        foreach (var seg in segments)
        {
            group.Children.Add(CreateLineModel(seg, color));
        }
        return group;
    }

    private static GeometryModel3D CreateLineModel(WireframeRenderer.Segment seg, Color color)
    {
        var line = new MeshGeometry3D();
        line.Positions.Add(new Point3D(seg.Start.X, seg.Start.Y, 0.01));
        line.Positions.Add(new Point3D(seg.End.X, seg.End.Y, 0.01));
        line.Freeze();
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var mat = new DiffuseMaterial(brush);
        mat.Freeze();
        return new GeometryModel3D(line, mat);
    }

    /// <summary>Rotate the view (degrees).</summary>
    public void Rotate(double degrees)
    {
        var cam = Camera;
        var dir = cam.LookDirection;
        var rot = new RotateTransform3D(new AxisAngleRotation3D(new Vector3D(0, 0, 1), degrees));
        var v = rot.Transform(dir);
        cam.LookDirection = v;
        cam.Position = (Point3D)(rot.Transform((Vector3D)cam.Position));
    }

    // ---- Toolpath simulation playback ----

    private SimulationPlayback? _playback;
    private System.Windows.Threading.DispatcherTimer? _timer;

    /// <summary>Load g-code for playback; renders the full wireframe overlay.</summary>
    public void LoadGcode(IReadOnlyList<string> lines)
    {
        _playback = new SimulationPlayback(lines);
        ShowToolpath(WireframeRenderer.GenerateSegments(lines));
        UpdateProgress();
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (_playback is null) return;
        if (_timer is null)
        {
            _timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _timer.Tick += (_, _) =>
            {
                if (_playback is { IsFinished: false })
                {
                    _playback.StepMany(1);
                    UpdateProgress();
                }
                else if (_timer is not null)
                {
                    _timer.Stop();
                    BtnPlay.Content = "▶";
                }
            };
        }
        if (_timer.IsEnabled)
        {
            _timer.Stop();
            BtnPlay.Content = "▶";
        }
        else
        {
            _timer.Start();
            BtnPlay.Content = "⏸";
        }
    }

    private void Step_Click(object sender, RoutedEventArgs e)
    {
        if (_playback is { IsFinished: false })
        {
            _playback.Step();
            UpdateProgress();
        }
    }

    private void Restart_Click(object sender, RoutedEventArgs e)
    {
        _playback?.Restart();
        UpdateProgress();
    }

    private void SpeedSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (_playback is not null) _playback.SpeedMultiplier = SpeedSlider.Value;
        if (TxtSpeed is not null) TxtSpeed.Text = $"{SpeedSlider.Value:0}×";
    }

    private void UpdateProgress()
    {
        if (TxtProgress is null || _playback is null) return;
        TxtProgress.Text = $"{_playback.Progress * 100:0}%";
    }
}
