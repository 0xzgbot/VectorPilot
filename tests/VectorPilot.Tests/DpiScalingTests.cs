using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VectorPilot.App;
using VectorPilot.App.Controls;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// The window must stay usable at high DPI, and E-STOP must never be clipped or scrolled
/// out of view.
///
/// Two real defects this pins:
///  * MainWindow was a hardcoded 1400x860 with NO MinWidth/MinHeight. At 150% scaling that
///    is 2100x1290 physical pixels — bigger than a 1920x1080 screen, so Windows shrinks the
///    window and inner content gets cut off.
///  * BtnEStop lived INSIDE the left ScrollViewer, so a short window could scroll the
///    e-stop off-screen while a job was streaming. It is now pinned in its own grid row.
/// </summary>
[Collection("STA")]
public class DpiScalingTests
{
    private static void OnSta(Action body)
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                // MainWindow references StaticResource PanelBg from App.xaml. A test host
                // has no Application, so the resource dictionary must be loaded explicitly
                // or every construction throws XamlParseException.
                if (Application.Current is null) _ = new Application();

                // The brushes live in App.xaml, whose root is an Application — so
                // LoadComponent cannot hand back a ResourceDictionary. Register the keys
                // MainWindow/MachinePanel actually bind to.
                var res = Application.Current!.Resources;
                if (!res.Contains("PanelBg"))
                {
                    // Every brush App.xaml defines. Missing ONE of these throws
                    // XamlParseException from a StaticResource lookup, so the list must be
                    // complete rather than "the ones I remembered".
                    res["RailBg"] = new SolidColorBrush(Color.FromRgb(0x19, 0x19, 0x22));
                    res["RailHover"] = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x3A));
                    res["Accent"] = new SolidColorBrush(Color.FromRgb(0x3D, 0x7E, 0xFF));
                    res["PanelBg"] = new SolidColorBrush(Color.FromRgb(0xF4, 0xF4, 0xF6));
                    res["TextOnDark"] = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xF0));
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

    /// <summary>Depth-first search for a named element in the visual/logical tree.</summary>
    private static T? Find<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T hit && hit.Name == name) return hit;

        if (root is FrameworkElement fe && fe.FindName(name) is T byName) return byName;

        int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (Find<T>(child, name) is { } found) return found;
        }
        return null;
    }

    private static bool HasScrollViewerAncestor(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is ScrollViewer) return true;
            node = System.Windows.Media.VisualTreeHelper.GetParent(node)
                   ?? System.Windows.LogicalTreeHelper.GetParent(node);
        }
        return false;
    }

    // ---- the window fits a 1080p screen at 150% ----

    [Fact]
    public void The_Window_Declares_A_Minimum_Size_That_Fits_1080p_At_150_Percent()
    {
        OnSta(() =>
        {
            var w = new MainWindow();

            Assert.True(w.MinWidth > 0, "MainWindow has no MinWidth — WPF would allow a 0-width window");
            Assert.True(w.MinHeight > 0, "MainWindow has no MinHeight");

            // At 150% DPI a logical unit is 1.5 physical pixels. The minimum must still fit
            // inside a 1920x1080 panel, or the window cannot be shown un-clipped at all.
            Assert.True(w.MinWidth * 1.5 <= 1920,
                $"MinWidth {w.MinWidth} needs {w.MinWidth * 1.5:F0}px at 150% — wider than 1920");
            Assert.True(w.MinHeight * 1.5 <= 1080,
                $"MinHeight {w.MinHeight} needs {w.MinHeight * 1.5:F0}px at 150% — taller than 1080");
        });
    }

    [Fact]
    public void The_Default_Size_Fits_1080p_At_100_Percent()
    {
        OnSta(() =>
        {
            var w = new MainWindow();
            Assert.True(w.Width <= 1920 && w.Height <= 1080,
                $"default {w.Width}x{w.Height} does not fit a 1080p screen");
        });
    }

    [Fact]
    public void The_Minimum_Is_Not_Larger_Than_The_Default()
    {
        OnSta(() =>
        {
            var w = new MainWindow();
            Assert.True(w.MinWidth <= w.Width);
            Assert.True(w.MinHeight <= w.Height);
        });
    }

    // ---- E-STOP is pinned, not scrollable ----

    [Fact]
    public void The_EStop_Button_Exists_And_Is_Enabled()
    {
        OnSta(() =>
        {
            var panel = new MachinePanel();
            panel.Measure(new Size(1400, 860));
            panel.Arrange(new Rect(0, 0, 1400, 860));

            var estop = Find<Button>(panel, "BtnEStop");
            Assert.NotNull(estop);
            Assert.True(estop!.IsEnabled, "E-STOP must never be gated on connection state");
        });
    }

    [Fact]
    public void The_EStop_Is_Not_Inside_A_ScrollViewer()
    {
        // The safety rule is "always visible". Inside a ScrollViewer it can be scrolled
        // away, which is exactly where it used to live.
        OnSta(() =>
        {
            var panel = new MachinePanel();
            panel.Measure(new Size(1400, 860));
            panel.Arrange(new Rect(0, 0, 1400, 860));

            var estop = Find<Button>(panel, "BtnEStop");
            Assert.NotNull(estop);
            Assert.False(HasScrollViewerAncestor(estop),
                "E-STOP sits inside a ScrollViewer — it can be scrolled out of view mid-job");
        });
    }

    [Fact]
    public void The_Reset_Button_Is_Also_Pinned()
    {
        OnSta(() =>
        {
            var panel = new MachinePanel();
            panel.Measure(new Size(1400, 860));
            panel.Arrange(new Rect(0, 0, 1400, 860));

            var reset = Find<Button>(panel, "BtnReset");
            Assert.NotNull(reset);
            Assert.False(HasScrollViewerAncestor(reset));
            Assert.True(reset!.IsEnabled);
        });
    }

    [Fact]
    public void The_EStop_Stays_Visible_In_A_Short_Window()
    {
        // 700 logical units tall is the declared minimum; the e-stop must still be laid
        // out with a real size inside it.
        OnSta(() =>
        {
            var panel = new MachinePanel();
            panel.Measure(new Size(1024, 700));
            panel.Arrange(new Rect(0, 0, 1024, 700));

            var estop = Find<Button>(panel, "BtnEStop");
            Assert.NotNull(estop);
            Assert.True(estop!.ActualHeight > 0,
                "E-STOP has zero height at the minimum window size — it is clipped away");
            Assert.True(estop.ActualWidth > 0, "E-STOP has zero width at the minimum window size");
        });
    }

    [Fact]
    public void The_EStop_Is_Big_Enough_To_Hit_At_High_DPI()
    {
        // A shrunken safety control is a safety problem: keep a real touch target.
        OnSta(() =>
        {
            var panel = new MachinePanel();
            panel.Measure(new Size(1024, 700));
            panel.Arrange(new Rect(0, 0, 1024, 700));

            var estop = Find<Button>(panel, "BtnEStop");
            Assert.NotNull(estop);
            Assert.True(estop!.ActualHeight >= 40,
                $"E-STOP is only {estop.ActualHeight:F0} units tall at minimum size");
        });
    }

    [Fact]
    public void The_Machine_Panel_Lays_Out_At_A_4K_Size()
    {
        OnSta(() =>
        {
            var panel = new MachinePanel();
            panel.Measure(new Size(3840, 2160));
            panel.Arrange(new Rect(0, 0, 3840, 2160));

            var estop = Find<Button>(panel, "BtnEStop");
            Assert.NotNull(estop);
            Assert.True(estop!.ActualHeight > 0);
        });
    }
}
