using System.Windows;
using System.Windows.Controls;
using VectorPilot.App;
using VectorPilot.App.Controls;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using VectorPilot.Serial;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// H-104: Frame + click-to-jog. Frame rapids the tool around the job bounds so the operator
/// SEES the travel before cutting; click-to-jog moves the head to a clicked canvas point.
/// Both are motion, so both are gated on a real connected session — the Frame button is
/// DISABLED until a machine is open, because offering motion that does nothing is worse
/// than offering none.
/// </summary>
[Collection("STA")]
public class FrameJogTests
{
    private static void OnSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                if (Application.Current is null) _ = new Application();
                var res = Application.Current!.Resources;
                if (!res.Contains("PanelBg"))
                {
                    foreach (var k in new[] { "RailBg", "RailHover", "Accent", "PanelBg", "TextOnDark" })
                        res[k] = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
                    res["RailButton"] = new Style(typeof(Button));
                }
                body();
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw error;
    }

    // ---- MachineSession.FrameAsync ----

    [Fact]
    public async Task Frame_Emits_A_Closed_G0_Rectangle_At_Safe_Z()
    {
        var session = new MachineSession(new SimulatorTransport());
        await session.ConnectAsync(new MachineProfile { Name = "Sim" });

        Assert.True(await session.FrameAsync(10, 20, 110, 70, feed: 1500, safeZ: 5));

        var log = session.ConsoleLog;
        var moves = log.Where(l => l.Contains(">> G0")).ToList();

        // lift, 4 corners, return to start = 6 rapid lines
        Assert.Equal(6, moves.Count);
        Assert.Contains("G0 Z5", moves[0]);
        Assert.Contains("G0 X10 Y20", moves[1]);   // start
        Assert.Contains("G0 X110 Y20", moves[2]);
        Assert.Contains("G0 X110 Y70", moves[3]);
        Assert.Contains("G0 X10 Y70", moves[4]);
        Assert.Contains("G0 X10 Y20", moves[5]);   // back to start — closed loop
        Assert.DoesNotContain(moves, m => m.Contains("G1"));
    }

    [Fact]
    public async Task Frame_Without_A_Connection_Is_Refused()
    {
        var session = new MachineSession(new SimulatorTransport());
        Assert.False(await session.FrameAsync(0, 0, 10, 10, 1000, 5));
    }

    // ---- FrameBounds: selection wins, else the sheet ----

    [Fact]
    public void Frame_Uses_The_Selection_Bounds_When_Something_Is_Selected()
    {
        OnSta(() =>
        {
            var panel = new DesignPanel();
            var layer = panel.ActiveLayer!;
            // Rectangle(x, y, WIDTH, HEIGHT): from (40,30) sized 80x60 → bounds (40,30)-(120,90).
            var shape = VectorShape.Rectangle(40, 30, 80, 60);
            layer.Shapes.Add(shape);
            panel.SelectForTest(shape);

            var (x0, y0, x1, y1) = panel.FrameBounds();
            Assert.Equal(40, x0, 3);
            Assert.Equal(30, y0, 3);
            Assert.Equal(120, x1, 3);
            Assert.Equal(90, y1, 3);
        });
    }

    [Fact]
    public void Frame_With_No_Selection_Covers_The_Whole_Sheet_In_Mm()
    {
        OnSta(() =>
        {
            var panel = new DesignPanel();
            // Default sheet is 12x24 INCHES; FrameAsync speaks mm, so 304.8 x 609.6.
            var (x0, y0, x1, y1) = panel.FrameBounds();

            Assert.Equal(0, x0, 3);
            Assert.Equal(0, y0, 3);
            Assert.Equal(304.8, x1, 1);
            Assert.Equal(609.6, y1, 1);
        });
    }

    // ---- the button is honest about connectivity ----

    [Fact]
    public void The_Frame_Button_Starts_Disabled_And_Never_Enables_Without_A_Session()
    {
        OnSta(() =>
        {
            var panel = new DesignPanel();
            var dock = new MachineDock();          // not connected
            panel.AttachMachineDock(dock);

            var frame = (Button)panel.FindName("FrameButton")!;
            Assert.False(frame.IsEnabled);         // motion is not offered while disconnected
        });
    }

    [Fact]
    public void The_Frame_Button_Enables_When_A_Genuinely_Connected_Session_Is_Adopted()
    {
        OnSta(() =>
        {
            var panel = new DesignPanel();
            var dock = new MachineDock();
            panel.AttachMachineDock(dock);

            // Adopt alone is not enough: IsConnected reads the session's own transport,
            // so an adopted-but-never-opened session must NOT enable motion. Connect first.
            var transport = new SimulatorTransport();
            var session = new MachineSession(transport);
            session.ConnectAsync(new MachineProfile { Name = "Sim" }).Wait();

            dock.Adopt(session, transport);   // raises DockMessage -> queued chrome update

            // Drain the Dispatcher so the BeginInvoke'd UpdateMotionChrome actually runs.
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                () => { }, System.Windows.Threading.DispatcherPriority.Background);

            var frame = (Button)panel.FindName("FrameButton")!;
            Assert.True(frame.IsEnabled);
        });
    }

    [Fact]
    public void An_Adopted_But_Unopened_Session_Does_Not_Enable_Motion()
    {
        OnSta(() =>
        {
            var panel = new DesignPanel();
            var dock = new MachineDock();
            panel.AttachMachineDock(dock);

            // A session object without an opened transport is not a machine.
            dock.Adopt(new MachineSession(new SimulatorTransport()), new SimulatorTransport());
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(
                () => { }, System.Windows.Threading.DispatcherPriority.Background);

            Assert.False(((Button)panel.FindName("FrameButton")!).IsEnabled);
        });
    }

    [Fact]
    public void Frame_Click_While_Disconnected_Reports_And_Does_Nothing()
    {
        OnSta(() =>
        {
            var panel = new DesignPanel();
            panel.AttachMachineDock(new MachineDock());

            // Direct handler call: the click path with no session must stay a no-op.
            panel.Frame_Click(null!, null!);
            Assert.Contains("connected", panel.LastStatus(), StringComparison.OrdinalIgnoreCase);
        });
    }

    // ---- click-to-jog ----

    [Fact]
    public async Task Click_To_Jog_Computes_A_Relative_Move_From_The_Dro()
    {
        // The jog delta is (clicked world point) - (DRO position). Pin that arithmetic at
        // the session boundary: a head at (30, 20) clicked at (100, 50) jogs +70, +30.
        var session = new MachineSession(new SimulatorTransport());
        await session.ConnectAsync(new MachineProfile { Name = "Sim" });
        session.SetDroForTest(30, 20);

        double curX = session.Dro.X is { } sx ? double.Parse(sx, System.Globalization.CultureInfo.InvariantCulture) : 0;
        double curY = session.Dro.Y is { } sy ? double.Parse(sy, System.Globalization.CultureInfo.InvariantCulture) : 0;
        double dx = 100 - curX;
        double dy = 50 - curY;

        Assert.Equal(70, dx, 3);
        Assert.Equal(30, dy, 3);
        Assert.True(await session.JogAsync(dx, dy, 0, 1500));
    }

    [Fact]
    public async Task Jog_Without_A_Connection_Is_Refused()
    {
        var session = new MachineSession(new SimulatorTransport());
        Assert.False(await session.JogAsync(10, 0, 0, 1000));
    }
}
