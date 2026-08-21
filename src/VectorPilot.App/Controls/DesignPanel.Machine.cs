using System.Windows;
using System.Windows.Input;
using VectorPilot.Engine;
using VectorPilot.Geometry;

namespace VectorPilot.App.Controls;

/// <summary>
/// H-104: machine interaction from the Design stage — Frame and click-to-jog.
///
/// Frame rapids the tool around the job (or selection) bounds so the operator can SEE the
/// travel before cutting; click-to-jog moves the head to a clicked canvas point when the
/// machine is connected. Both go through the app-lifetime session on the MachineDock, so
/// they work identically no matter which stage connected.
///
/// The button starts DISABLED and is enabled only while a session is open: motion that is
/// offered while disconnected is a lie — clicking it would do nothing, and the user would
/// believe their work area was framed when it never moved.
/// </summary>
public partial class DesignPanel
{
    private MachineDock? _dock;

    /// <summary>Wire to the app-lifetime dock. Called by MainWindow after construction.</summary>
    public void AttachMachineDock(MachineDock dock)
    {
        _dock = dock ?? throw new ArgumentNullException(nameof(dock));
        _dock.DockMessage += _ => Dispatcher.BeginInvoke(new Action(UpdateMotionChrome));
        UpdateMotionChrome();
    }

    /// <summary>Frame is only offered while a machine session is actually open.</summary>
    private void UpdateMotionChrome()
    {
        if (FrameButton is null) return;
        bool canMove = _dock?.Session?.IsConnected == true;
        FrameButton.IsEnabled = canMove;
    }

    /// <summary>
    /// The rectangle Frame will trace: the selection's bounds when shapes are selected,
    /// else the whole sheet. World coordinates, mm.
    /// </summary>
    public (double X0, double Y0, double X1, double Y1) FrameBounds()
    {
        var layer = ActiveLayer;
        var selected = layer?.Shapes.Where(Selection.Selected.Contains).ToList();

        double minX = 0, minY = 0, maxX = 0, maxY = 0;
        if (selected is { Count: > 0 })
        {
            minX = selected.Min(s => s.Points.Min(p => p.X));
            minY = selected.Min(s => s.Points.Min(p => p.Y));
            maxX = selected.Max(s => s.Points.Max(p => p.X));
            maxY = selected.Max(s => s.Points.Max(p => p.Y));
        }
        else
        {
            // Sheet dims are stored with a UnitSystem; FrameAsync speaks mm, so convert.
            var sheet = AppState.CurrentJob?.ActiveSheet
                        ?? new Sheet();
            double toMm = sheet.Units == UnitSystem.Inches ? 25.4 : 1.0;
            minX = 0; minY = 0;
            maxX = sheet.Width * toMm;
            maxY = sheet.Height * toMm;
        }

        return (minX, minY, maxX, maxY);
    }

    public async void Frame_Click(object sender, RoutedEventArgs e)
    {
        var session = _dock?.Session;
        if (session is not { IsConnected: true })
        {
            SetStatus("Frame needs a connected machine — connect on the Machine stage or the dock.");
            return;
        }

        var (x0, y0, x1, y1) = FrameBounds();
        // Safe Z and feed: modest defaults a hobby router survives. The operator adjusts
        // feed on the machine; framing is a visual check, not a cutting pass.
        bool ok = await session.FrameAsync(x0, y0, x1, y1, feed: 1500, safeZ: 5);
        SetStatus(ok
            ? $"Framed ({x0:0.#},{y0:0.#}) → ({x1:0.#},{y1:0.#}) at safe Z"
            : "Frame failed — machine did not accept the move");
    }

    /// <summary>
    /// Click-to-jog: with a machine connected AND the Select tool active, Ctrl+Click sends
    /// the head to the clicked world point. Plain clicks keep behaving exactly as before
    /// (selection), because remapping them would make the design surface unusable.
    /// </summary>
    private async void Canvas_MouseDown_ForJog(MouseButtonEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        if (CurrentTool != Tool.Select) return;

        var session = _dock?.Session;
        if (session is not { IsConnected: true }) return;

        var world = ScreenToWorld(e.GetPosition(DrawCanvas));

        // Where is the head now? Use the DRO's last reported WPos; if we have never seen a
        // status report, refuse rather than assume 0,0.
        var dro = session.Dro;
        if (!TryParseDro(dro.X, out double curX) || !TryParseDro(dro.Y, out double curY))
        {
            SetStatus("Jog skipped: no position report yet — wait for a status poll.");
            return;
        }

        double dx = world.X - curX;
        double dy = world.Y - curY;
        bool ok = await session.JogAsync(dx, dy, 0, feed: 1500);
        SetStatus(ok
            ? $"Jogging to ({world.X:0.#}, {world.Y:0.#})"
            : "Jog failed — machine did not accept it");
    }

    private static bool TryParseDro(string text, out double value)
        => double.TryParse(text, System.Globalization.NumberStyles.Float,
                           System.Globalization.CultureInfo.InvariantCulture, out value);

    // ---- test seams (no production behaviour) ----

    public void SelectForTest(VectorShape shape)
    {
        Selection.Select(shape);
        RedrawShapes();
    }

    public string LastStatus() => StatusLabel.Text;
}
