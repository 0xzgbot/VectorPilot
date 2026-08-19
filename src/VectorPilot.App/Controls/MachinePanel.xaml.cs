using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using VectorPilot.Engine;
using VectorPilot.Serial;

namespace VectorPilot.App.Controls;

public partial class MachinePanel : UserControl
{
    public event Action<string>? RailStatusChanged;
    public event Action<string>? DocumentTitleChanged;

    private bool _connected;
    private IMachineTransport? _transport;
    private readonly DispatcherTimer _pollTimer;
    private string _pollState = "";

    public MachinePanel()
    {
        InitializeComponent();
        RefreshPorts();
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _pollTimer.Tick += (_, _) => PollStatus();
        Loaded += (_, _) =>
        {
            RefreshPorts();
            UpdateStreamButtons();
            // Wired here, not in XAML: attaching in XAML fires during init before
            // sibling elements exist.
            ConsoleToggle.Checked += ConsoleToggle_Changed;
            ConsoleToggle.Unchecked += ConsoleToggle_Changed;
        };
        Unloaded += (_, _) => _pollTimer.Stop();
    }

    private void RefreshPorts()
    {
        CmbPort.Items.Clear();
        CmbPort.Items.Add("SIMULATOR — virtual GRBL");
        foreach (var p in SerialPortEnumerator.EnumeratePorts())
            CmbPort.Items.Add(p);
        CmbPort.SelectedIndex = 0;
    }

    private void Log(string line)
    {
        ConsoleText.Text += line + "\n";
        if (ConsoleText.Text.Length > 200_000) ConsoleText.Text = ConsoleText.Text[^100_000..];
        ConsoleScroller.ScrollToEnd();
    }

    private async void BtnConnect_Click(object sender, RoutedEventArgs e)
    {
        if (_connected)
        {
            await DisconnectAsync();
            return;
        }

        var selected = CmbPort.SelectedItem?.ToString() ?? "";
        var profile = selected.StartsWith("SIMULATOR")
            ? MachineProfile.Simulator()
            : new MachineProfile { Name = selected, PortName = selected, BaudRate = 115200 };

        _transport = selected.StartsWith("SIMULATOR") ? new SimulatorTransport() : new SerialTransport();
        AppState.ReplaceTransport(_transport);
        _transport.EventReceived += OnTransportEvent;

        try
        {
            await _transport.OpenAsync(profile);
            _connected = true;
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
    }

    private async Task DisconnectAsync()
    {
        _pollTimer.Stop();
        try
        {
            if (_transport is not null)
            {
                _transport.EventReceived -= OnTransportEvent;
                await _transport.CloseAsync();
            }
        }
        catch { /* ignore */ }
        _connected = false;
        ConnState.Text = "disconnected";
        ConnState.Foreground = System.Windows.Media.Brushes.Orange;
        BtnConnect.Content = "Connect";
        RailStatusChanged?.Invoke("disconnected");
        DroState.Text = DroMpos.Text = DroWpos.Text = DroFs.Text = DroBuf.Text = "—";
    }

    private void OnTransportEvent(TransportEvent ev)
    {
        Dispatcher.BeginInvoke(() =>
        {
            switch (ev.Type)
            {
                case TransportEventType.Opened:
                    Log($"> {ev.Payload}");
                    break;
                case TransportEventType.Closed:
                    Log($"> {ev.Payload}");
                    break;
                case TransportEventType.DataReceived:
                    Log(ev.Payload.StartsWith("$J") || ev.Payload.StartsWith("G") || ev.Payload.StartsWith("M")
                        ? $"TX: {ev.Payload}"
                        : $"RX: {ev.Payload}");
                    break;
                case TransportEventType.Status:
                    HandleStatus(ev.Payload);
                    break;
                case TransportEventType.Alarm:
                    Log($"⚠ {ev.Payload}");
                    DroState.Text = "ALARM";
                    DroState.Foreground = System.Windows.Media.Brushes.Red;
                    break;
                case TransportEventType.Error:
                    Log($"✖ {ev.Payload}");
                    break;
                case TransportEventType.Ok:
                    break;
                default:
                    Log($"{ev.Type}: {ev.Payload}");
                    break;
            }
        });
    }

    private void HandleStatus(string raw)
    {
        var p = StatusParser.Parse(raw);
        if (p is null) return;
        DroState.Text = p.State;
        DroState.Foreground = p.State switch
        {
            "Run" => System.Windows.Media.Brushes.LimeGreen,
            "Alarm" => System.Windows.Media.Brushes.Red,
            "Hold" => System.Windows.Media.Brushes.Orange,
            _ => System.Windows.Media.Brushes.Orange
        };
        DroMpos.Text = $"{p.MPosX:F3}  {p.MPosY:F3}  {p.MPosZ:F3}";
        DroWpos.Text = $"{p.WPosX:F3}  {p.WPosY:F3}  {p.WPosZ:F3}";
        DroFs.Text = p.FS is { } fs ? $"{fs.Feed:F0}  {fs.Spindle:F0} rpm" : "—";
        DroBuf.Text = p.Buffer?.ToString() ?? "—";
        _pollState = p.State;
    }

    private async void PollStatus()
    {
        if (_connected && _transport is not null)
        {
            try { await _transport.WriteLineAsync("?"); }
            catch { /* ignore */ }
        }
    }

    private async void SendLine(string line)
    {
        if (_transport is { IsOpen: true })
        {
            try { await _transport.WriteLineAsync(line); }
            catch (Exception ex) { Log($"✖ send failed: {ex.Message}"); }
        }
    }

    private double StepSize
    {
        get
        {
            var item = CmbStep.SelectedItem as ComboBoxItem;
            var label = item?.Content as string;
            return label switch
            {
                "1.0" => 1.0,
                "0.1" => 0.1,
                "0.01" => 0.01,
                "0.001" => 0.001,
                _ => double.NaN // continuous
            };
        }
    }

    private void Jog(string axis, double sign)
    {
        if (!_connected) return;
        var step = StepSize;
        var feed = ParseOr(TxtJogFeed.Text, 200);
        var cmd = double.IsNaN(step)
            ? $"$J=G91{axis}0.0F{feed.ToString("F0", CultureInfo.InvariantCulture)}" // continuous jog stub (single tick)
            : $"$J=G91{axis}{sign * step:F3}F{feed.ToString("F0", CultureInfo.InvariantCulture)}";
        SendLine(cmd);
    }

    private void JogXPlus(object s, RoutedEventArgs e) => Jog("X", 1);
    private void JogXMinus(object s, RoutedEventArgs e) => Jog("X", -1);
    private void JogYPlus(object s, RoutedEventArgs e) => Jog("Y", 1);
    private void JogYMinus(object s, RoutedEventArgs e) => Jog("Y", -1);
    private void JogZMinus(object s, RoutedEventArgs e) => Jog("Z", -1);

    private void BtnHome(object s, RoutedEventArgs e) => SendLine("$H");
    private void BtnZeroXY(object s, RoutedEventArgs e) => SendLine("G10 L20 P1 X0 Y0");
    private void BtnZeroZ(object s, RoutedEventArgs e) => SendLine("G10 L20 P1 Z0");
    private void BtnUnlock(object s, RoutedEventArgs e) => SendLine("$X");
    private void BtnSpindleOn(object s, RoutedEventArgs e) => SendLine("M3 S12000");
    private void BtnSpindleOff(object s, RoutedEventArgs e) => SendLine("M5");

    // ---- Stream ----

    private void BtnLoad_Click(object sender, RoutedEventArgs e)
    {
        // If the Cut stage handed over G-code, offer to use it first.
        if (AppState.LoadedGCode.Count > 0)
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
        BtnStart.IsEnabled = hasGcode && _connected;
        BtnPause.IsEnabled = _connected && AppState.Streamer?.Phase == StreamPhase.Streaming;
        BtnResume.IsEnabled = _connected && AppState.Streamer?.Phase == StreamPhase.Paused;
        BtnStop.IsEnabled = _connected && AppState.Streamer?.Phase is StreamPhase.Streaming or StreamPhase.Paused;
    }

    private async void StreamStart_Click(object sender, RoutedEventArgs e)
    {
        if (AppState.LoadedGCode.Count == 0) return;
        var streamer = AppState.EnsureStreamer();
        streamer.ProgressChanged += sp => Dispatcher.BeginInvoke(() =>
        {
            StreamProgress.Value = sp.TotalLines == 0 ? 0 : sp.CurrentLine * 100.0 / sp.TotalLines;
            StreamInfo.Text = $"{sp.CurrentLine} / {sp.TotalLines} lines · {sp.Phase}";
            UpdateStreamButtons();
        });

        try
        {
            await streamer.StartAsync(AppState.LoadedGCode);
            StreamInfo.Text = "complete ✓";
        }
        catch (Exception ex)
        {
            StreamInfo.Text = $"halted: {ex.Message}";
            Log($"✖ stream: {ex.Message}");
        }
        finally
        {
            UpdateStreamButtons();
        }
    }

    private void StreamPause_Click(object sender, RoutedEventArgs e)
    {
        AppState.Streamer?.Pause();
        SendLine("!"); // real-time feed hold
        UpdateStreamButtons();
    }

    private void StreamResume_Click(object sender, RoutedEventArgs e)
    {
        AppState.Streamer?.Resume();
        SendLine("~");
        UpdateStreamButtons();
    }

    private void StreamStop_Click(object sender, RoutedEventArgs e)
    {
        AppState.Streamer?.Cancel();
        SendLine("\u0018"); // 0x18 soft reset
        SendLine("$X");    // unlock
        UpdateStreamButtons();
    }

    // ---- Card A5 safety chrome: always enabled, never gated on state ----

    private async void EStop_Click(object sender, RoutedEventArgs e)
    {
        Log(">> ! (E-STOP)");
        AppState.Streamer?.Cancel();
        if (_transport is not null) await _transport.PauseAsync();
        UpdateStreamButtons();
        RailStatusChanged?.Invoke("E-STOP engaged");
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        Log(">> 0x18 (soft reset)");
        AppState.Streamer?.Cancel();
        if (_transport is not null) await _transport.WriteLineAsync("\u0018");
        UpdateStreamButtons();
        RailStatusChanged?.Invoke("Soft reset sent");
    }

    private void ConsoleToggle_Changed(object sender, RoutedEventArgs e)
    {
        // Fires during XAML init before the sibling elements exist.
        if (ConsoleScroller is null || ConsoleText is null) return;

        bool on = ConsoleToggle.IsChecked == true;
        ConsoleScroller.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        if (!on) ConsoleText.Text = "(console off)";
    }

    private static double ParseOr(string s, double fallback)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
}
