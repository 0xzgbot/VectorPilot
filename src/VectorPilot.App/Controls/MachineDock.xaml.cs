using System.Windows;
using System.Windows.Controls;
using VectorPilot.Serial;

namespace VectorPilot.App.Controls;

/// <summary>
/// App-lifetime machine dock (gSender/LightBurn pattern): the machine is always present,
/// not something you visit. The dock lives in the MainWindow shell, outside the stage host,
/// so E-stop / Hold / Reset stay visible and enabled on every stage.
///
/// Session ownership moves HERE (App lifetime) instead of living on MachinePanel, whose
/// Unloaded handler stops its poll timer the moment you leave the stage.
/// </summary>
public partial class MachineDock : UserControl
{
    /// <summary>The app-lifetime session. Created on connect, kept across stage switches.</summary>
    public MachineSession? Session { get; private set; }

    /// <summary>Raised after any dock action, so panels can refresh DRO/console.</summary>
    public event Action<string>? DockMessage;

    /// <summary>Raised when the user asks for the full Machine stage (Connect…).</summary>
    public event Action? MachineStageRequested;

    public MachineDock()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Connect through the dock. The simulator path needs no stage visit; a real port still
    /// sends the user to the Machine stage where the port picker lives.
    /// </summary>
    public async Task<bool> ConnectSimulatorAsync()
    {
        var profile = MachineProfile.Simulator();
        var transport = new SimulatorTransport();
        return await ConnectAsync(profile, transport);
    }

    /// <summary>Shared connect path. Keeps the session alive on the dock, not the panel.</summary>
    public async Task<bool> ConnectAsync(MachineProfile profile, IMachineTransport transport)
    {
        if (Session is not null)
        {
            try { await Session.DisconnectAsync(); } catch { /* replacing */ }
        }

        var session = new MachineSession(transport);
        var ok = false;
        try
        {
            ok = await session.ConnectAsync(profile);
        }
        catch (Exception ex)
        {
            DockNote.Text = $"connect failed: {ex.Message}";
        }

        if (!ok)
        {
            DockConnState.Text = "machine: connect failed";
            return false;
        }

        Session = session;
        AppState.ReplaceTransport(transport);
        AppState.Profile = profile;
        DockConnState.Text = $"connected · {transport.Name}";
        DockConnState.Foreground = System.Windows.Media.Brushes.LimeGreen;
        DockMessage?.Invoke($"dock: connected · {transport.Name}");
        return true;
    }

    /// <summary>Hand the session to the dock when the Machine panel connected first.</summary>
    public void Adopt(MachineSession session, IMachineTransport transport)
    {
        Session = session;
        AppState.ReplaceTransport(transport);
        DockConnState.Text = $"connected · {transport.Name}";
        DockConnState.Foreground = System.Windows.Media.Brushes.LimeGreen;
        // State changed behind the panels' backs — let chrome re-evaluate (Frame button etc).
        DockMessage?.Invoke($"dock: adopted · {transport.Name}");
    }

    /// <summary>Reflect connection state changes driven from the Machine panel.</summary>
    public void MarkDisconnected()
    {
        Session = null;
        DockConnState.Text = "machine: not connected";
        DockConnState.Foreground = System.Windows.Media.Brushes.Orange;
    }

    /// <summary>Test seam: re-raise the dock message so listeners re-evaluate chrome.</summary>
    public void RaiseDockMessageForTest() => DockMessage?.Invoke("dock: state changed");

    // ---- safety chrome: never gated on connection or stream state ----

    private async void DockEStop_Click(object sender, RoutedEventArgs e)
    {
        if (Session is null)
        {
            DockNote.Text = "E-STOP: not connected";
            DockMessage?.Invoke("E-STOP: not connected");
            return;
        }
        await Session.EmergencyStopAsync();
        DockNote.Text = "E-STOP engaged";
        DockMessage?.Invoke("E-STOP engaged (dock)");
    }

    private async void DockHold_Click(object sender, RoutedEventArgs e)
    {
        if (Session is null)
        {
            DockNote.Text = "Hold: not connected";
            DockMessage?.Invoke("Hold: not connected");
            return;
        }
        // MachineSession has no Hold method (verified); the realtime '!' / '~' pair lives on
        // the transport. Hold here means "pause the cycle".
        await Session.Transport.PauseAsync();
        DockNote.Text = "feed hold sent";
        DockMessage?.Invoke("hold toggled (dock)");
    }

    private async void DockReset_Click(object sender, RoutedEventArgs e)
    {
        if (Session is null)
        {
            DockNote.Text = "Reset: not connected";
            DockMessage?.Invoke("Reset: not connected");
            return;
        }
        await Session.ResetAsync();
        DockNote.Text = "soft reset sent";
        DockMessage?.Invoke("soft reset sent (dock)");
    }

    private void DockConnect_Click(object sender, RoutedEventArgs e)
    {
        // The port picker lives on the Machine stage; the dock routes the user there.
        MachineStageRequested?.Invoke();
    }

    /// <summary>H-401: open the touch-plate probe wizard over the app shell.</summary>
    private void DockProbe_Click(object sender, RoutedEventArgs e)
    {
        var owner = Window.GetWindow(this);
        var dlg = new ProbeWizardDialog(this) { Owner = owner };
        dlg.ShowDialog();
    }

    /// <summary>H-401 test seam: construct the wizard exactly as the button does.</summary>
    public ProbeWizardDialog OpenProbeWizard() => new(this);
}
