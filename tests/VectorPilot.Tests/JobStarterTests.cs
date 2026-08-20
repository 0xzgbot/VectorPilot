using System.Windows;
using System.Windows.Controls;
using VectorPilot.App;
using VectorPilot.App.Controls;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// H-101: Beginner/Advanced modes and the three job starters.
///
/// The registry grew to 28 strategies and the first thing a new user met was a combo box
/// containing "Thread Mill" and "Wrapped Fluting". These tests call UiModeCatalog and
/// JobStarterOverlay.ApplyStarter — the same code the rail combo and the starter buttons
/// invoke — so a green test cannot coexist with a dead button.
/// </summary>
[Collection("STA")]
public class JobStarterTests
{
    private static readonly StrategyRegistry Reg = new();

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
                    res["RailBg"] = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x19, 0x19, 0x22));
                    res["RailHover"] = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x26, 0x26, 0x3A));
                    res["Accent"] = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0x3D, 0x7E, 0xFF));
                    res["PanelBg"] = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xF4, 0xF4, 0xF6));
                    res["TextOnDark"] = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(0xE8, 0xE8, 0xF0));
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

    // ---- Beginner hides the intimidating operations ----

    [Fact]
    public void Beginner_Offers_At_Most_Eight_Operations()
    {
        var visible = UiModeCatalog.Filter(UiMode.Beginner, Reg.Entries, e => e.Key);

        Assert.True(visible.Count <= UiModeCatalog.BeginnerMaxOperations,
            $"Beginner offered {visible.Count} operations");
        Assert.NotEmpty(visible);
    }

    [Fact]
    public void Beginner_Does_Not_Show_Thread_Mill()
    {
        // The card's acceptance criterion, verbatim.
        var visible = UiModeCatalog.Filter(UiMode.Beginner, Reg.Entries, e => e.Key);

        Assert.DoesNotContain(visible, e => e.Key == "threadmill");
        Assert.DoesNotContain(visible, e => e.DisplayName.Contains("Thread", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Beginner_Hides_The_Specialist_Operations()
    {
        var visible = UiModeCatalog.Filter(UiMode.Beginner, Reg.Entries, e => e.Key)
            .Select(e => e.Key).ToHashSet();

        foreach (var specialist in new[] { "threadmill", "wrapped-fluting", "rotary-wrap", "drill-bank", "weave" })
            Assert.DoesNotContain(specialist, visible);
    }

    [Fact]
    public void Advanced_Shows_The_Whole_Registry()
    {
        var visible = UiModeCatalog.Filter(UiMode.Advanced, Reg.Entries, e => e.Key);

        Assert.Equal(Reg.Entries.Count, visible.Count);
        Assert.Contains(visible, e => e.Key == "threadmill");
    }

    [Fact]
    public void Advanced_Offers_Strictly_More_Than_Beginner()
    {
        Assert.True(
            UiModeCatalog.Filter(UiMode.Advanced, Reg.Entries, e => e.Key).Count >
            UiModeCatalog.Filter(UiMode.Beginner, Reg.Entries, e => e.Key).Count);
    }

    [Fact]
    public void Every_Beginner_Key_Really_Exists_In_The_Registry()
    {
        // A typo here silently shrinks Beginner mode. "quick-engrave" was exactly that bug:
        // the real key is "quickengrave".
        var keys = Reg.Entries.Select(e => e.Key).ToHashSet();

        foreach (var key in UiModeCatalog.BeginnerKeys)
            Assert.Contains(key, keys);
    }

    [Fact]
    public void Beginner_Keeps_The_Everyday_Operations()
    {
        var visible = UiModeCatalog.Filter(UiMode.Beginner, Reg.Entries, e => e.Key)
            .Select(e => e.Key).ToHashSet();

        foreach (var everyday in new[] { "profile", "pocket", "vcarve" })
            Assert.Contains(everyday, visible);
    }

    [Fact]
    public void The_Filter_Preserves_Beginner_Order()
    {
        var visible = UiModeCatalog.Filter(UiMode.Beginner, Reg.Entries, e => e.Key)
            .Select(e => e.Key).ToList();

        Assert.Equal("profile", visible[0]);   // cut a shape out comes first
    }

    [Fact]
    public void IsVisible_Agrees_With_The_Filter()
    {
        foreach (var entry in Reg.Entries)
        {
            bool inList = UiModeCatalog
                .Filter(UiMode.Beginner, Reg.Entries, e => e.Key)
                .Any(e => e.Key == entry.Key);

            Assert.Equal(inList, UiModeCatalog.IsVisible(UiMode.Beginner, entry.Key));
            Assert.True(UiModeCatalog.IsVisible(UiMode.Advanced, entry.Key));
        }
    }

    // ---- the three starters do something concrete ----

    [Theory]
    [InlineData(JobStarterKind.Sign)]
    [InlineData(JobStarterKind.Photo)]
    [InlineData(JobStarterKind.ThreeD)]
    public void Every_Starter_Selects_A_Real_Registry_Strategy(JobStarterKind kind)
    {
        var key = JobStarterOverlay.ApplyStarter(kind);

        Assert.False(string.IsNullOrWhiteSpace(key));
        Assert.Contains(key, Reg.Entries.Select(e => e.Key));
    }

    [Fact]
    public void The_Starters_Choose_Different_Strategies()
    {
        var keys = new[] { JobStarterKind.Sign, JobStarterKind.Photo, JobStarterKind.ThreeD }
            .Select(JobStarterOverlay.ApplyStarter)
            .ToList();

        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void The_Sign_Starter_Lands_On_VCarve()
    {
        Assert.Equal("vcarve", JobStarterOverlay.ApplyStarter(JobStarterKind.Sign));
    }

    [Fact]
    public void The_Photo_Starter_Lands_On_A_Photo_Strategy()
    {
        Assert.Equal("photo-vcarve", JobStarterOverlay.ApplyStarter(JobStarterKind.Photo));
    }

    [Fact]
    public void The_3D_Starter_Lands_On_A_Relief_Strategy()
    {
        Assert.Equal("rough3d", JobStarterOverlay.ApplyStarter(JobStarterKind.ThreeD));
    }

    [Fact]
    public void Every_Starter_Has_A_Label()
    {
        foreach (var kind in new[] { JobStarterKind.Sign, JobStarterKind.Photo, JobStarterKind.ThreeD })
            Assert.False(string.IsNullOrWhiteSpace(UiModeCatalog.Label(kind)));
    }

    // ---- the XAML is real and the buttons are wired ----

    [Fact]
    public void The_Overlay_Constructs_With_All_Three_Starter_Buttons()
    {
        OnSta(() =>
        {
            var overlay = new JobStarterOverlay();

            foreach (var name in new[] { "BtnStarterSign", "BtnStarterPhoto", "BtnStarter3D", "BtnStarterSkip" })
            {
                var button = overlay.FindName(name) as Button;
                Assert.NotNull(button);
                Assert.True(button!.IsEnabled, $"{name} is disabled");
            }
        });
    }

    [Fact]
    public void The_Overlay_Raises_Started_With_The_Chosen_Strategy()
    {
        OnSta(() =>
        {
            var overlay = new JobStarterOverlay();

            JobStarterKind? seenKind = null;
            string? seenKey = null;
            overlay.Started += (k, key) => { seenKind = k; seenKey = key; };

            // Raise the same click the button raises.
            var button = (Button)overlay.FindName("BtnStarterSign")!;
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            Assert.Equal(JobStarterKind.Sign, seenKind);
            Assert.Equal("vcarve", seenKey);
        });
    }

    [Fact]
    public void Skipping_Switches_To_Advanced()
    {
        OnSta(() =>
        {
            AppState.UiMode = UiMode.Beginner;
            var overlay = new JobStarterOverlay();

            bool raised = false;
            overlay.Started += (_, _) => raised = true;

            var button = (Button)overlay.FindName("BtnStarterSkip")!;
            button.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

            Assert.True(raised, "Skip did not raise Started");
            Assert.Equal(UiMode.Advanced, AppState.UiMode);
        });
    }

    // ---- the Cut panel honours the mode ----

    [Fact]
    public void The_Cut_Combo_Shrinks_In_Beginner_And_Grows_In_Advanced()
    {
        OnSta(() =>
        {
            AppState.UiMode = UiMode.Beginner;
            var panel = new CutPanel();
            var combo = (ComboBox)panel.FindName("CmbStrategy")!;

            int beginner = combo.Items.Count;
            Assert.True(beginner <= UiModeCatalog.BeginnerMaxOperations,
                $"Beginner combo had {beginner} items");

            AppState.UiMode = UiMode.Advanced;
            panel.RefreshForMode();

            Assert.True(combo.Items.Count > beginner,
                $"Advanced combo had {combo.Items.Count}, Beginner had {beginner}");
        });
    }

    [Fact]
    public void Selecting_A_Hidden_Strategy_Switches_To_Advanced()
    {
        // The Photo starter picks photo-vcarve, which is not a Beginner operation. It must
        // still land there rather than leaving the user on whatever was selected.
        OnSta(() =>
        {
            AppState.UiMode = UiMode.Beginner;
            var panel = new CutPanel();

            Assert.True(panel.SelectStrategy("photo-vcarve"), "could not select photo-vcarve");
            Assert.Equal(UiMode.Advanced, AppState.UiMode);

            var combo = (ComboBox)panel.FindName("CmbStrategy")!;
            var selected = (StrategyRegistry.Entry)combo.SelectedItem!;
            Assert.Equal("photo-vcarve", selected.Key);
        });
    }

    [Fact]
    public void Selecting_A_Beginner_Strategy_Stays_In_Beginner()
    {
        OnSta(() =>
        {
            AppState.UiMode = UiMode.Beginner;
            var panel = new CutPanel();

            Assert.True(panel.SelectStrategy("pocket"));
            Assert.Equal(UiMode.Beginner, AppState.UiMode);
        });
    }

    [Fact]
    public void A_Beginner_Starter_Does_Not_Silently_Promote_To_Advanced()
    {
        // Sign -> vcarve is a Beginner operation, so choosing it must LEAVE the user in
        // Beginner. Only a hidden strategy (Photo -> photo-vcarve) may promote.
        OnSta(() =>
        {
            AppState.UiMode = UiMode.Beginner;
            var panel = new CutPanel();

            Assert.True(panel.SelectStrategy(JobStarterOverlay.ApplyStarter(JobStarterKind.Sign)));
            Assert.Equal(UiMode.Beginner, AppState.UiMode);
        });
    }

    [Fact]
    public void The_Beginner_Ceiling_Tracks_The_Key_List()
    {
        // Derived, not a hand-kept constant that can drift from BeginnerKeys.
        Assert.Equal(UiModeCatalog.BeginnerKeys.Length, UiModeCatalog.BeginnerMaxOperations);
    }

    [Fact]
    public void The_Main_Window_Hosts_The_Mode_Combo_And_Starter_Button()
    {
        OnSta(() =>
        {
            var w = new MainWindow();

            Assert.NotNull(w.FindName("CmbUiMode") as ComboBox);
            var starter = w.FindName("BtnJobStarter") as Button;
            Assert.NotNull(starter);
            Assert.True(starter!.IsEnabled);

            // The overlay host must start hidden, or automated startup would block on it.
            var host = w.FindName("StarterHost") as ContentControl;
            Assert.NotNull(host);
            Assert.Equal(Visibility.Collapsed, host!.Visibility);
        });
    }
}
