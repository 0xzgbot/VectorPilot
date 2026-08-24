using VectorPilot.App;
using VectorPilot.App.Controls;
using VectorPilot.Serial;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// H-403: rotary mode. Y words wrap to A (deg = mm / Ø × 360) at SEND time —
/// the program on disk stays flat, the transport receives A words. The toggle
/// never sends motion by itself, and the simulator tracks and wraps the A axis.
/// </summary>
[Collection("STA")]
public class RotaryWrapTests
{
    private static MachineSession ConnectedSession(out SimulatorTransport transport)
    {
        transport = new SimulatorTransport();
        var session = new MachineSession(transport);
        session.ConnectAsync(new MachineProfile { Name = "Sim" }).GetAwaiter().GetResult();
        return session;
    }

    [Fact]
    public void Wrap_Converts_Y_Linear_To_A_Degrees_By_Circumference()
    {
        var session = ConnectedSession(out _);
        session.SetRotaryMode(true, diameterMm: 50);   // circumference ≈ 157.08

        // 157.0796mm of linear travel = one full revolution = 360°.
        string wrapped = session.WrapYToA("G1 X10 Y157.0796 F1200");
        var a = System.Text.RegularExpressions.Regex.Match(wrapped, @"A(\d+(?:\.\d+)?)");
        Assert.True(a.Success, $"no A word in '{wrapped}'");
        Assert.Equal(360, double.Parse(a.Groups[1].Value), 0);
    }

    [Fact]
    public void Wrap_Preserves_The_Rest_Of_The_Line_And_Off_Mode_Sends_As_Is()
    {
        var session = ConnectedSession(out var transport);
        session.SetRotaryMode(true, 50);

        string wrapped = session.WrapYToA("G0 A90 Y25");
        Assert.Contains("A", wrapped);
        // The Y word is gone — replaced by the A word (which keeps its own value).
        var afterFirstA = wrapped[wrapped.IndexOf('A')..];
        Assert.DoesNotContain(" Y", afterFirstA);

        // Non-motion lines pass through untouched.
        Assert.Equal("M3 S18000", session.WrapYToA("M3 S18000"));
        Assert.Equal("(comment)", session.WrapYToA("(comment)"));

        // Off mode: SendWithRotaryWrapAsync forwards verbatim.
        session.SetRotaryMode(false, 50);
        session.SendWithRotaryWrapAsync("G1 Y42").GetAwaiter().GetResult();
        Assert.Contains(transport.ConsoleLinesForTest, l => l.Contains("Y42"));
        Assert.DoesNotContain(transport.ConsoleLinesForTest, l => l.Contains("A95."));
    }

    [Fact]
    public void Simulator_Tracks_And_Wraps_Explicit_A_Words()
    {
        var session = ConnectedSession(out var transport);

        session.SendWithRotaryWrapAsync("G0 A270").GetAwaiter().GetResult();
        Assert.Equal(270, transport.AAxisDegrees, 3);

        session.SendWithRotaryWrapAsync("G0 A450").GetAwaiter().GetResult();   // wraps to 90
        Assert.Equal(90, transport.AAxisDegrees, 3);

        session.SendWithRotaryWrapAsync("G0 A-45").GetAwaiter().GetResult();   // wraps to 315
        Assert.Equal(315, transport.AAxisDegrees, 3);
    }

    [Fact]
    public void Toggle_Never_Starts_Motion_And_Off_Is_The_Default()
    {
        // MachineDock is a UI control — construct it on a private STA thread. It
        // deliberately does NOT create/require an Application: MachineDock.xaml
        // uses literal brushes only, and a second Application per AppDomain throws.
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                var session = ConnectedSession(out var transport);
                Assert.False(session.RotaryModeEnabled);   // default off

                var dock = new MachineDock();
                dock.Adopt(session, transport);
                bool state = dock.ToggleRotaryForTest(enable: true, diameterMm: 80);

                Assert.True(state);
                Assert.True(session.RotaryModeEnabled);
                Assert.Equal(80, session.RotaryDiameterMm, 3);

                int logBefore = session.ConsoleLog.Count;
                bool offState = dock.ToggleRotaryForTest(enable: false, diameterMm: 80);
                // The toggle returns the RESULTING state — off now.
                Assert.False(offState);
                Assert.False(session.RotaryModeEnabled);
                // Toggling itself sent no motion lines — only "-- rotary mode" markers.
                var newLines = session.ConsoleLog.Skip(logBefore).ToList();
                Assert.All(newLines, l => Assert.DoesNotContain(l, "G0 "));
                Assert.All(newLines, l => Assert.DoesNotContain(l, "G1 "));
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        Assert.True(t.Join(TimeSpan.FromSeconds(30)), "test hung");
        if (error is not null) throw error;
    }
}
