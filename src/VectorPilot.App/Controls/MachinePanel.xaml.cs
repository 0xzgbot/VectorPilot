using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using VectorPilot.Engine;
using VectorPilot.Serial;

namespace VectorPilot.App.Controls;

/// <summary>
/// Machine stage. ALL machine access goes through a single <see cref="MachineSession"/>
/// (card A5) — this panel holds no transport or streamer of its own, so the safety
/// invariants covered by MachineSessionTests are the ones that actually run here.
/// </summary>
public partial class MachinePanel : UserControl
{
    public event Action<string>? RailStatusChanged;
    public event Action<string>? DocumentTitleChanged;

    /// <summary>The one session this panel drives. Internal so tests can inspect it.</summary>
    internal MachineSession? Session { get; private set; }

    private readonly DispatcherTimer _pollTimer;
    private int _consoleShown;

    public MachinePanel()
    {
        InitializeComponent();
        RefreshPorts();
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _pollTimer.Tick += async (_, _) => await PollAsync();
        Loaded += (_, _) =>
        {
            RefreshPorts();
            UpdateStreamButtons();
            // Wired here, not in XAML: XAML-attached handlers fire during init
            // before sibling elements exist (caused a startup NRE once already).
            ConsoleToggle.Checked += ConsoleToggle_Changed;
            ConsoleToggle.Unchecked += ConsoleToggle_Changed;
        };
        Unloaded += (_, _) => _pollTimer.Stop();
    }

    private bool Connected => Session?.IsConnected == true;

    private void RefreshPorts()
    {
        CmbPort.Items.Clear();
        CmbPort.Items.Add("SIMULATOR — virtual GRBL");
        foreach (var p in SerialPortEnumerator.EnumeratePorts())
            CmbPort.Items.Add(p);
        CmbPort.SelectedIndex = 0;
    }

    /// <summary>Mirror the session's console buffer into the view.</summary>
    private void PumpConsole()
    {
        if (Session is null || ConsoleToggle.IsChecked != true) return;

        var log = Session.ConsoleLog;
        for (; _consoleShown < log.Count; _consoleShown++)
            ConsoleText.Text += log[_consoleShown] + "\n";

        if (ConsoleText.Text.Length > 200_000) ConsoleText.Text = ConsoleText.Text[^100_000..];
        ConsoleScroller.ScrollToEnd();
    }

    // ---- connection ----

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        if (Connected) { await DisconnectAsync(); return; }

        var selected = CmbPort.SelectedItem?.ToString() ?? "";
        bool sim = selected.StartsWith("SIMULATOR");
        var profile = sim
            ? MachineProfile.Simulator()
            : new MachineProfile { Name = selected, PortName = selected, BaudRate = 115200 };

        var transport = sim ? new SimulatorTransport() : (IMachineTransport)new SerialTransport();
        AppState.ReplaceTransport(transport);

        Session = new MachineSession(transport);
        Session.Alarm += OnSessionAlarm;
        _consoleShown = 0;

        try
        {
            if (!await Session.ConnectAsync(profile)) throw new IOException("port did not open");
            AppState.Profile = profile;
            ConnState.Text = $"connected · {selected}";
            ConnState.Foreground = System.Windows.Media.Brushes.LimeGreen;
            BtnConnect.Content = "Disconnect";
            _pollTimer.Start();
            RailStatusChanged?.Invoke($"connected · {selected}");
            DocumentTitleChanged?.Invoke("New Job — connected");
        }
        catch (Exception ex)
        {
            ConnState.Text = $"failed: {ex.Message}";
            ConnState.Foreground = System.Windows.Media.Brushes.Red;
        }
        PumpConsole();
        UpdateStreamButtons();
    }

    private async Task DisconnectAsync()
    {
        _pollTimer.Stop();
        if (Session is not null)
        {
            try { await Session.DisconnectAsync(); } catch { /* ignore */ }
            Session.Alarm -= OnSessionAlarm;
        }
        ConnState.Text = "disconnected";
        ConnState.Foreground = System.Windows.Media.Brushes.Orange;
        BtnConnect.Content = "Connect";
        RailStatusChanged?.Invoke("disconnected");
        DroState.Text = DroMpos.Text = DroWpos.Text = DroFs.Text = DroBuf.Text = "—";
        PumpConsole();
        UpdateStreamButtons();
    }

    private void OnSessionAlarm(string message) => Dispatcher.BeginInvoke(() =>
    {
        DroState.Text = "ALARM";
        DroState.Foreground = System.Windows.Media.Brushes.Red;
        RailStatusChanged?.Invoke(message);
        PumpConsole();
        UpdateStreamButtons();
    });

    private async Task PollAsync()
    {
        if (Session is null) return;
        await Session.PollAsync();

        var dro = Session.Dro;
        DroState.Text = dro.State;
        DroState.Foreground = dro.State switch
        {
            "Run" => System.Windows.Media.Brushes.LimeGreen,
            "Alarm" => System.Windows.Media.Brushes.Red,
            _ => System.Windows.Media.Brushes.Orange
        };
        DroMpos.Text = $"{dro.X}  {dro.Y}  {dro.Z}";
        DroWpos.Text = $"{dro.X}  {dro.Y}  {dro.Z}";
        DroFs.Text = $"{dro.Feed}  {dro.Spindle} rpm";
        PumpConsole();
    }

    // ---- jog ----

    private double StepSize
    {
        get
        {
            var label = (CmbStep.SelectedItem as ComboBoxItem)?.Content as string;
            return label switch
            {
                "1.0" => 1.0,
                "0.1" => 0.1,
                "0.01" => 0.01,
                "0.001" => 0.001,
                _ => double.NaN   // continuous
            };
        }
    }

    private async void Jog(string axis, double sign)
    {
        if (Session is null) return;
        double feed = ParseOr(TxtJogFeed.Text, 200);
        double step = StepSize;

        if (double.IsNaN(step))
            await Session.JogContinuousAsync(axis, sign, feed);   // real travel, not $J=…0.0
        else
            await Session.JogAsync(
                axis == "X" ? sign * step : 0,
                axis == "Y" ? sign * step : 0,
                axis == "Z" ? sign * step : 0,
                feed);

        PumpConsole();
    }

    private void JogXPlus(object s, RoutedEventArgs e) => Jog("X", 1);
    private void JogXMinus(object s, RoutedEventArgs e) => Jog("X", -1);
    private void JogYPlus(object s, RoutedEventArgs e) => Jog("Y", 1);
    private void JogYMinus(object s, RoutedEventArgs e) => Jog("Y", -1);
    private void JogZMinus(object s, RoutedEventArgs e) => Jog("Z", -1);

    private async void BtnHome(object s, RoutedEventArgs e) { if (Session is not null) { await Session.SoftHomeAsync(); PumpConsole(); } }
    private async void BtnZeroXY(object s, RoutedEventArgs e) => await Send("G10 L20 P1 X0 Y0");
    private async void BtnZeroZ(object s, RoutedEventArgs e) => await Send("G10 L20 P1 Z0");
    private async void BtnUnlock(object s, RoutedEventArgs e) => await Send("$X");
    private async void BtnSpindleOn(object s, RoutedEventArgs e) => await Send("M3 S12000");
    private async void BtnSpindleOff(object s, RoutedEventArgs e) => await Send("M5");

    private async Task Send(string line)
    {
        if (Session is null) return;
        await Session.SendAsync(line);
        PumpConsole();
    }

    // ---- stream ----

    private void BtnLoad_Click(object sender, RoutedEventArgs e)
    {
        if (AppState.LoadedGCode.Count > 0 && !App.IsAutomated)
        {
            var r = MessageBox.Show("Use the G-code calculated in the Toolpaths stage?", "VectorPilot",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes)
            {
                TxtGcodePath.Text = "(from Toolpaths stage)";
                UpdateStreamButtons();
                return;
            }
        }
        var dlg = new OpenFileDialog
        {
            Filter = "G-code files (*.tap;*.nc;*.gcode;*.gco)|*.tap;*.nc;*.gcode;*.gco|All files (*.*)|*.*",
            Title = "Load G-code"
        };
        if (dlg.ShowDialog() == true)
        {
            AppState.LoadedGCode = File.ReadAllLines(dlg.FileName).ToList();
            AppState.LoadedGCodePath = dlg.FileName;
            TxtGcodePath.Text = Path.GetFileName(dlg.FileName);
            StreamInfo.Text = $"0 / {AppState.LoadedGCode.Count} lines";
            UpdateStreamButtons();
        }
    }

    private void UpdateStreamButtons()
    {
        bool hasGcode = AppState.LoadedGCode.Count > 0;
        bool streaming = Session?.IsStreaming == true;

        BtnStart.IsEnabled = hasGcode && Connected && !streaming;
        BtnPause.IsEnabled = Connected && streaming;
        BtnResume.IsEnabled = Connected && !streaming;
        BtnStop.IsEnabled = Connected && streaming;
        // E-STOP and Reset are deliberately NOT touched here — always enabled.
    }

    private async void StreamStart_Click(object sender, RoutedEventArgs e)
    {
        if (Session is null || AppState.LoadedGCode.Count == 0) return;

        // Same gate Cut's Calculate uses. Start previously streamed AppState.LoadedGCode
        // with NO validation, so a comment-only program — which looks runnable and which the
        // controller happily accepts — put the operator in front of a job that never cuts.
        if (JobGate.StreamBlocker(AppState.LoadedGCode) is { } blocker)
        {
            if (!App.IsAutomated)
            {
                MessageBox.Show(blocker, "Cannot start", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            // This panel has no AppendConsole helper; it writes ConsoleText directly.
            if (ConsoleText is not null) ConsoleText.Text += $"[blocked] {blocker}\n";
            return;
        }

        var progress = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        progress.Tick += (_, _) =>
        {
            int total = Session.TotalLines, cur = Session.StreamedLines;
            StreamProgress.Value = total == 0 ? 0 : cur * 100.0 / total;
            StreamInfo.Text = $"{cur} / {total} lines";
            if (Session.IsStreaming) return;
            progress.Stop();
            UpdateStreamButtons();
        };
        progress.Start();

        try
        {
            await Session.StartStreamAsync(AppState.LoadedGCode);
            StreamInfo.Text = "complete ✓";
        }
        catch (Exception ex)
        {
            StreamInfo.Text = $"halted: {ex.Message}";
        }
        finally
        {
            progress.Stop();
            PumpConsole();
            UpdateStreamButtons();
        }
    }

    private async void StreamPause_Click(object sender, RoutedEventArgs e)
    {
        if (Session is null) return;
        await Session.PauseStreamAsync();
        PumpConsole();
        UpdateStreamButtons();
    }

    private async void StreamResume_Click(object sender, RoutedEventArgs e)
    {
        if (Session is null) return;
        await Session.ResumeAsync();
        PumpConsole();
        UpdateStreamButtons();
    }

    private async void StreamStop_Click(object sender, RoutedEventArgs e)
    {
        if (Session is null) return;
        await Session.ResetAsync();
        await Session.SendAsync("$X");
        PumpConsole();
        UpdateStreamButtons();
    }

    // ---- safety chrome: always enabled, never gated on state ----

    private async void EStop_Click(object sender, RoutedEventArgs e)
    {
        if (Session is null) { RailStatusChanged?.Invoke("E-STOP: not connected"); return; }
        await Session.EmergencyStopAsync();
        PumpConsole();
        UpdateStreamButtons();
        RailStatusChanged?.Invoke("E-STOP engaged");
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (Session is null) { RailStatusChanged?.Invoke("Reset: not connected"); return; }
        await Session.ResetAsync();
        PumpConsole();
        UpdateStreamButtons();
        RailStatusChanged?.Invoke("Soft reset sent");
    }

    private void ConsoleToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (ConsoleScroller is null || ConsoleText is null) return;

        bool on = ConsoleToggle.IsChecked == true;
        ConsoleScroller.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (Session is not null) Session.ConsoleEnabled = on;
        if (!on) ConsoleText.Text = "(console off)";
    }

    private static double ParseOr(string s, double fallback)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
