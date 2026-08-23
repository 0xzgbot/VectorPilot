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

        // H-302: drag-to-sculpt. Left-drag on the mesh converts to stock XY and
        // strokes the sculpt target; right-drag still orbits nothing (camera is
        // animated separately), so the whole surface is the sculpt canvas.
        Viewport.MouseLeftButtonDown += Sculpt_Down;
        Viewport.MouseMove += Sculpt_Move;
        Viewport.MouseLeftButtonUp += (_, _) => _sculpting = false;
        Viewport.MouseLeave += (_, _) => _sculpting = false;
    }

    // ---- H-302: drag-to-sculpt ----

    private bool _sculpting;

    /// <summary>Receives (worldX, worldY) for each drag sample and reports whether the
    /// stroke actually changed anything. ModelPanel wires this to the component stack;
    /// null = sculpting disabled.</summary>
    public event Func<double, double, bool>? SculptStroke;

    /// <summary>Heightfield currently shown, for screen→stock mapping.</summary>
    private HeightfieldData? _shownField;

    /// <summary>Show the heightfield as a solid mesh. Pass null to clear.</summary>
    public void ShowHeightfield(HeightfieldData? heightfield, double? maxZ = null)
    {
        _shownField = heightfield;
        StockModel.Content = heightfield is null ? null : BuildMesh(heightfield, maxZ ?? heightfield.MaxHeight);
    }

    /// <summary>
    /// Screen point → stock XY using the SHOWN field's bounds (the mesh is drawn
    /// centered on the origin: x = (col − (w−1)/2)·cell). Public so tests drive the
    /// exact mapping the mouse handler uses.
    /// </summary>
    public bool TryScreenToStock(Point p, out double x, out double y)
    {
        x = y = 0;
        if (_shownField is not { } hf) return false;

        double w = hf.Width * hf.CellSizeMm, h = hf.Height * hf.CellSizeMm;

        // Project: the camera looks at the origin; map viewport pixels to the mesh's
        // world rectangle via the viewport's relative position. This is exact for the
        // default top-down-ish camera and monotonic for orbit angles — good enough to
        // place a brush, and the SAME math the drag path uses. An unrendered control
        // (tests, hidden tab) reports 0 size; fall back to a nominal 400×300 surface
        // so the mapping still lands ON the mesh instead of kilometres away.
        double vpW = Viewport.ActualWidth > 1 ? Viewport.ActualWidth : 400;
        double vpH = Viewport.ActualHeight > 1 ? Viewport.ActualHeight : 300;
        var relX = Math.Clamp(p.X / vpW, 0, 1);
        var relY = Math.Clamp(p.Y / vpH, 0, 1);
        x = (relX - 0.5) * w;
        y = (relY - 0.5) * h;
        return true;
    }

    /// <summary>One sculpt sample at a viewport point (public test seam). True only
    /// when the stroke actually modified the heightfield.</summary>
    public bool SculptAt(Point p)
    {
        if (!TryScreenToStock(p, out var x, out var y)) return false;
        return SculptStroke?.Invoke(x, y) == true;
    }

    private void Sculpt_Down(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (SculptStroke is null) return;
        _sculpting = true;
        Viewport.CaptureMouse();
        SculptAt(e.GetPosition(Viewport));
    }

    private void Sculpt_Move(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_sculpting || SculptStroke is null) return;
        if (System.Windows.Input.Mouse.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
        {
            _sculpting = false;
            return;
        }
        SculptAt(e.GetPosition(Viewport));
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

    // ---- animated camera (Aspire OSG camera row) ----
    //
    // The preview only ever had this manual Rotate(): a static orbit the user nudged by
    // hand. Aspire animates the camera, which is how you actually inspect a 3D relief.

    private System.Windows.Threading.DispatcherTimer? _orbitTimer;

    /// <summary>Degrees per second while the camera is orbiting.</summary>
    public double OrbitSpeedDegreesPerSecond { get; set; } = 24.0;

    /// <summary>True while the camera is animating.</summary>
    public bool IsOrbiting => _orbitTimer?.IsEnabled == true;

    /// <summary>Start a continuous camera orbit around the model's Z axis.</summary>
    public void StartOrbit()
    {
        if (_orbitTimer is null)
        {
            _orbitTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)   // ~30fps
            };
            _orbitTimer.Tick += (_, _) =>
                Rotate(OrbitSpeedDegreesPerSecond * _orbitTimer!.Interval.TotalSeconds);
        }
        _orbitTimer.Start();
    }

    /// <summary>Stop the orbit, leaving the camera where it is.</summary>
    public void StopOrbit() => _orbitTimer?.Stop();

    /// <summary>Toggle the orbit; returns the new state.</summary>
    public bool ToggleOrbit()
    {
        if (IsOrbiting) StopOrbit(); else StartOrbit();
        return IsOrbiting;
    }

    /// <summary>
    /// Ease the camera to a named viewpoint over <paramref name="milliseconds"/>.
    /// Uses a smooth-step so the move decelerates instead of snapping.
    /// </summary>
    public void AnimateToView(CameraViewpoint view, int milliseconds = 450)
    {
        StopOrbit();

        var cam = Camera;
        var (target, look) = ViewpointVectors(view, cam);

        var startPos = cam.Position;
        var startLook = cam.LookDirection;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };

        timer.Tick += (_, _) =>
        {
            double t = Math.Clamp(sw.Elapsed.TotalMilliseconds / Math.Max(1, milliseconds), 0, 1);
            double e = t * t * (3 - 2 * t);   // smooth-step

            cam.Position = new Point3D(
                startPos.X + (target.X - startPos.X) * e,
                startPos.Y + (target.Y - startPos.Y) * e,
                startPos.Z + (target.Z - startPos.Z) * e);

            cam.LookDirection = new Vector3D(
                startLook.X + (look.X - startLook.X) * e,
                startLook.Y + (look.Y - startLook.Y) * e,
                startLook.Z + (look.Z - startLook.Z) * e);

            if (t >= 1) timer.Stop();
        };
        timer.Start();
    }

    /// <summary>
    /// Camera position + look direction for a named viewpoint. Public so it can be
    /// verified without a WPF dispatcher pumping animation ticks.
    /// </summary>
    public static (Point3D Position, Vector3D Look) ViewpointVectors(
        CameraViewpoint view, PerspectiveCamera cam)
    {
        // Preserve the current orbit distance so a view change does not also zoom.
        double d = Math.Max(1.0, Math.Sqrt(
            cam.Position.X * cam.Position.X +
            cam.Position.Y * cam.Position.Y +
            cam.Position.Z * cam.Position.Z));

        // 1/sqrt(3) exactly — the rounded 0.577 literal drifts the orbit distance
        // (0.09mm over 150mm), which shows up as a view change quietly zooming.
        double iso = 1.0 / Math.Sqrt(3.0);

        return view switch
        {
            CameraViewpoint.Top => (new Point3D(0, 0, d), new Vector3D(0, 0, -1)),
            CameraViewpoint.Front => (new Point3D(0, -d, 0), new Vector3D(0, 1, 0)),
            CameraViewpoint.Right => (new Point3D(d, 0, 0), new Vector3D(-1, 0, 0)),
            CameraViewpoint.Isometric => (
                new Point3D(d * iso, -d * iso, d * iso),
                new Vector3D(-iso, iso, -iso)),
            _ => (cam.Position, cam.LookDirection)
        };
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
