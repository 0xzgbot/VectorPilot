using System.Windows;
using VectorPilot.Serial;

namespace VectorPilot.App.Controls;

/// <summary>
/// H-401: gSender-style Z touch-plate wizard. Collects plate thickness / probe
/// target / feed, runs MachineSession.ProbeZAsync (G38.2), and reports honestly —
/// a failed probe says so instead of zeroing anyway.
/// </summary>
public partial class ProbeWizardDialog : Window
{
    private readonly MachineDock _dock;

    public ProbeWizardDialog(MachineDock dock)
    {
        InitializeComponent();
        _dock = dock;
    }

    /// <summary>Run the probe with the CURRENT field values. Public seam: tests
    /// drive this exact path instead of clicking through the modal (the test
    /// project has no InternalsVisibleTo).</summary>
    public async Task<ProbeResult> RunProbeAsync()
    {
        double plate = Parse(TxtPlateThickness.Text, 10);
        double target = Parse(TxtTargetZ.Text, -15);
        double feed = Parse(TxtFeed.Text, 100);

        var session = _dock.Session;
        if (session is null)
        {
            ResultLabel.Text = "Not connected — connect first (nothing probed).";
            return new ProbeResult { Success = false, Reason = ResultLabel.Text };
        }

        BtnProbe.IsEnabled = false;
        ProbeBar.Visibility = Visibility.Visible;
        try
        {
            var result = await session.ProbeZAsync(target, feed, plate);
            ResultLabel.Text = result.Success
                ? $"✔ {result.Reason}"
                : $"✘ Probe failed: {result.Reason} — work zero NOT changed.";
            return result;
        }
        finally
        {
            BtnProbe.IsEnabled = true;
            ProbeBar.Visibility = Visibility.Collapsed;
        }
    }

    private async void Probe_Click(object sender, RoutedEventArgs e) => await RunProbeAsync();

    /// <summary>The dialog can only complete a probe when a session exists — the
    /// button is dead otherwise (AC: no motion if disconnected).</summary>
    public bool CanProbe => _dock.Session is not null && IsConnected(_dock.Session);

    private static bool IsConnected(MachineSession s) => s.IsConnected;

    private static double Parse(string? text, double fallback)
        => double.TryParse(text?.Trim(), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private void Close_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
