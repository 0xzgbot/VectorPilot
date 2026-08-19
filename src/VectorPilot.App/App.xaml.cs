using System.Windows;
using System.Windows.Threading;
using VectorPilot.App.Controls;
using VectorPilot.Engine;

namespace VectorPilot.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"VectorPilot error:\n{args.Exception.Message}", "VectorPilot", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // --ui-smoke: exercise every stage + dialog at runtime, then exit 0.
        // Used by the launch smoke test; never run in normal use.
        if (e.Args.Contains("--ui-smoke"))
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            // A one-shot timer, not a dispatcher priority: panels run their own
            // 250ms DispatcherTimers, which starve Idle/Background callbacks.
            var kick = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(600)
            };
            kick.Tick += (_, _) => { kick.Stop(); RunUiSmoke(); };
            kick.Start();
        }
    }

    private static readonly string SmokeLog = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "vp-ui-smoke.log");

    private static void Log(string line)
    {
        Console.WriteLine(line);
        System.IO.File.AppendAllText(SmokeLog, line + Environment.NewLine);
    }

    private void RunUiSmoke()
    {
        System.IO.File.WriteAllText(SmokeLog, $"SMOKE start {DateTime.Now:O}{Environment.NewLine}");
        try
        {
            var main = new MainWindow();
            main.Show();
            Log("SMOKE: MainWindow shown");

            // Exercise each rail stage (initializes the panels).
            foreach (var tag in new[] { "design", "cut", "machine", "output" })
            {
                foreach (var button in FindVisualChildren<System.Windows.Controls.Button>(main))
                {
                    if (button.Tag as string == tag)
                    {
                        button.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                        break;
                    }
                }
                Pump();
                Log($"SMOKE: stage {tag} exercised");
            }

            // Exercise every dialog shell — each isolated so one failure
            // doesn't mask the rest.
            SmokeDialog("MaterialDialog", () => new MaterialDialog { Owner = main });
            SmokeDialog("PostManagerDialog", () => new PostManagerDialog { Owner = main });
            SmokeDialog("MachineConfigDialog", () => new MachineConfigDialog { Owner = main });
            SmokeDialog("CommandPaletteWindow", () => new CommandPaletteWindow(new CommandRegistry()) { Owner = main });
            SmokeDialog("PreferencesWindow", () => new PreferencesWindow { Owner = main });

            main.Close();
            Log("SMOKE: ALL OK");
            Shutdown(0);
        }
        catch (Exception ex)
        {
            Log($"SMOKE FAIL: {ex}");
            Shutdown(1);
        }
    }

    private void SmokeDialog(string name, Func<Window> factory)
    {
        Log($"SMOKE: opening {name}");
        try
        {
            var dlg = factory();
            dlg.Show();
            Pump();
            dlg.Close();
            Log($"SMOKE: {name} OK");
        }
        catch (Exception ex)
        {
            Log($"SMOKE: {name} THREW: {ex.Message}");
            throw;
        }
    }

    private static System.Collections.Generic.IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match) yield return match;
            foreach (var sub in FindVisualChildren<T>(child)) yield return sub;
        }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
