using VectorPilot.App;
using VectorPilot.Serial;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Card A5 — machine panel session. The safety invariants (E-stop always
/// available, no auto-start, disconnect-mid-stream alarms) are the point.
/// </summary>
public class MachinePanelTests
{
    private static MachineProfile Profile() => new() { PortName = "SIM", BaudRate = 115200 };

    private static (MachineSession Session, SimulatorTransport Sim) NewSession()
    {
        var sim = new SimulatorTransport();
        return (new MachineSession(sim), sim);
    }

    [Fact]
    public async Task Connect_Reports_Open()
    {
        var (s, _) = NewSession();
        Assert.False(s.IsConnected);
        Assert.True(await s.ConnectAsync(Profile()));
        Assert.True(s.IsConnected);
        await s.DisposeAsync();
    }

    [Fact]
    public async Task Nothing_Auto_Starts_On_Connect()
    {
        var (s, _) = NewSession();
        await s.ConnectAsync(Profile());
        Assert.False(s.IsStreaming);          // the invariant: no program on connect
        Assert.Equal(0, s.StreamedLines);
        await s.DisposeAsync();
    }

    [Fact]
    public async Task EStop_Works_Even_When_Disconnected()
    {
        var (s, _) = NewSession();
        string? alarm = null;
        s.Alarm += a => alarm = a;

        await s.EmergencyStopAsync();          // never gated on connection state
        Assert.Contains("E-STOP", alarm);
        await s.DisposeAsync();
    }

    [Fact]
    public async Task EStop_Cancels_An_Active_Stream()
    {
        var (s, _) = NewSession();
        await s.ConnectAsync(Profile());
        await s.StartStreamAsync(new[] { "G0 X1", "G1 X2 F100", "G1 X3 F100" });

        await s.EmergencyStopAsync();
        Assert.False(s.IsStreaming);
        await s.DisposeAsync();
    }

    [Fact]
    public async Task Reset_Sends_Soft_Reset_And_Clears_Streaming()
    {
        var (s, _) = NewSession();
        await s.ConnectAsync(Profile());
        await s.StartStreamAsync(new[] { "G0 X1", "G1 X2 F100" });

        await s.ResetAsync();
        Assert.False(s.IsStreaming);
        Assert.Contains(s.ConsoleLog, l => l.Contains("soft reset"));
        await s.DisposeAsync();
    }

    [Fact]
    public async Task Disconnect_Mid_Stream_Raises_An_Alarm()
    {
        var (s, _) = NewSession();
        string? alarm = null;
        s.Alarm += a => alarm = a;

        await s.ConnectAsync(Profile());
        await s.StartStreamAsync(new[] { "G0 X1", "G1 X2 F100", "G1 X3 F100" });
        await s.DisconnectAsync();

        Assert.False(s.IsStreaming);
        Assert.Contains("Disconnected while streaming", alarm);
        await s.DisposeAsync();
    }

    [Fact]
    public async Task Clean_Disconnect_Does_Not_Alarm()
    {
        var (s, _) = NewSession();
        string? alarm = null;
        s.Alarm += a => alarm = a;

        await s.ConnectAsync(Profile());
        await s.DisconnectAsync();             // never streamed
        Assert.Null(alarm);
        await s.DisposeAsync();
    }

    [Fact]
    public async Task Jog_Requires_A_Connection()
    {
        var (s, _) = NewSession();
        Assert.False(await s.JogAsync(1, 0, 0, 500));    // refused while closed

        await s.ConnectAsync(Profile());
        Assert.True(await s.JogAsync(1, 0, 0, 500));
        Assert.Contains(s.ConsoleLog, l => l.Contains("jog"));
        await s.DisposeAsync();
    }

    [Fact]
    public async Task Home_And_Work_Zero_Require_A_Connection()
    {
        var (s, _) = NewSession();
        Assert.False(await s.SoftHomeAsync());
        Assert.False(await s.SetWorkZeroAsync());

        await s.ConnectAsync(Profile());
        Assert.True(await s.SoftHomeAsync());
        Assert.True(await s.SetWorkZeroAsync());
        Assert.Contains(s.ConsoleLog, l => l.Contains("$H"));
        Assert.Contains(s.ConsoleLog, l => l.Contains("G10 L20"));
        await s.DisposeAsync();
    }

    [Fact]
    public async Task Stream_Refuses_When_Closed_Or_Empty()
    {
        var (s, _) = NewSession();
        Assert.False(await s.StartStreamAsync(new[] { "G0 X1" }));   // closed

        await s.ConnectAsync(Profile());
        Assert.False(await s.StartStreamAsync(Array.Empty<string>())); // empty
        await s.DisposeAsync();
    }

    [Fact]
    public async Task Stream_Does_Not_Restart_While_Already_Streaming()
    {
        var (s, _) = NewSession();
        await s.ConnectAsync(Profile());
        await s.StartStreamAsync(new[] { "G0 X1", "G1 X2 F100", "G1 X3 F100" });

        Assert.False(await s.StartStreamAsync(new[] { "G0 X9" }));   // second call rejected
        await s.DisposeAsync();
    }

    [Fact]
    public async Task Dro_Updates_From_Status_Lines()
    {
        var (s, sim) = NewSession();
        await s.ConnectAsync(Profile());
        await s.JogAsync(5, 5, 0, 800);
        await Task.Delay(150);

        Assert.NotNull(s.Dro);
        Assert.False(string.IsNullOrWhiteSpace(s.Dro.Readout));
        await s.DisposeAsync();
    }

    [Fact]
    public async Task Console_Can_Be_Toggled_Off()
    {
        var (s, _) = NewSession();
        s.ConsoleEnabled = false;
        await s.ConnectAsync(Profile());
        await s.JogAsync(1, 0, 0, 500);
        Assert.Empty(s.ConsoleLog);
        await s.DisposeAsync();
    }
}
