using System.Windows;
using System.Windows.Controls;
using VectorPilot.App;
using VectorPilot.App.Controls;
using VectorPilot.Serial;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// H-103: the machine dock. The machine is always on screen — E-stop / Hold / Reset live in
/// a pinned shell strip that survives leaving the Machine stage, and the session lives at
/// app lifetime (on the dock) instead of dying with the panel's Unloaded handler.
///
/// Safety chrome is never gated on connection or stream state, so the AC is structural:
/// the buttons exist on the dock, they are enabled with NO session, and they work against
/// a real connected session. No auto-start anywhere.
/// </summary>
[Collection("STA")]
public class MachineDockTests
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

    [Fact]
    public void The_Dock_Constructs_With_All_Safety_Buttons()
    {
        OnSta(() =>
        {
            var dock = new MachineDock();

            var stop = dock.FindName("BtnDockEStop") as Button;
            var hold = dock.FindName("BtnDockHold") as Button;
            var reset = dock.FindName("BtnDockReset") as Button;

            Assert.NotNull(stop);
            Assert.NotNull(hold);
            Assert.NotNull(reset);

            // Never gated on state.
            Assert.True(stop!.IsEnabled);
            Assert.True(hold!.IsEnabled);
            Assert.True(reset!.IsEnabled);
        });
    }

    [Fact]
    public void Safety_Buttons_Report_Not_Connected_Instead_Of_Crashing()
    {
        OnSta(() =>
        {
            var dock = new MachineDock();
            string? seen = null;
            dock.DockMessage += s => seen = s;

            ((Button)dock.FindName("BtnDockEStop")!).RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            Assert.Contains("not connected", seen);
        });
    }

    [Fact]
    public void A_Connected_Dock_EStops_A_Real_Session()
    {
        MachineDock? dock = null;
        bool connected = false;

        // The whole flow — construct, connect, click — must run on ONE STA thread; WPF
        // controls cannot be created on a thread-pool thread.
        var t = new Thread(async () =>
        {
            try
            {
                if (Application.Current is null) _ = new Application();
                dock = new MachineDock();
                connected = await dock.ConnectSimulatorAsync();

                ((Button)dock.FindName("BtnDockEStop")!).RaiseEvent(
                    new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                await Task.Delay(200);   // let the async-void handler drain
            }
            catch
            {
                // surfaced via the asserts below
            }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();

        Assert.True(connected, "simulator connect failed");
        Assert.NotNull(dock);
        Assert.NotNull(dock!.Session);
        Assert.True(dock.Session!.IsConnected);   // E-stop must not tear down the link
    }

    [Fact]
    public void Adopt_Takes_Ownerhip_Of_A_Panel_Created_Session()
    {
        OnSta(() =>
        {
            var dock = new MachineDock();
            var session = new MachineSession(new SimulatorTransport());

            dock.Adopt(session, new SimulatorTransport());

            Assert.Same(session, dock.Session);
            Assert.Contains("connected", (dock.FindName("DockConnState") as TextBlock)!.Text,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MarkDisconnected_Clears_The_Session()
    {
        OnSta(() =>
        {
            var dock = new MachineDock();
            dock.Adopt(new MachineSession(new SimulatorTransport()), new SimulatorTransport());

            dock.MarkDisconnected();

            Assert.Null(dock.Session);
            Assert.Contains("not connected", (dock.FindName("DockConnState") as TextBlock)!.Text,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Connect_Dot_Dot_Dot_Asks_For_The_Machine_Stage()
    {
        OnSta(() =>
        {
            var dock = new MachineDock();
            bool requested = false;
            dock.MachineStageRequested += () => requested = true;

            ((Button)dock.FindName("BtnDockConnect")!).RaiseEvent(
                new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            Assert.True(requested, "Connect… did not request the Machine stage");
        });
    }

    [Fact]
    public void No_Auto_Start_On_Construction()
    {
        OnSta(() =>
        {
            var dock = new MachineDock();
            Assert.Null(dock.Session);   // nothing connects by itself
        });
    }
}
