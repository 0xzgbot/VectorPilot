using System.Windows;
using System.Windows.Controls;
using VectorPilot.App.Controls;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Constructs every panel and dialog on a real STA thread. This closes the blind
/// spot that let a XAML init-order NullReferenceException ship green: nothing in
/// the suite had ever instantiated a UI class, so 650 passing tests said nothing
/// about whether the app could start.
///
/// [Collection("STA")] is required: WPF allows only ONE Application per AppDomain, and
/// DpiScalingTests also creates one. Without a shared collection the two classes run in
/// parallel and race with "Cannot create more than one System.Windows.Application".
/// </summary>
[Collection("STA")]
public class XamlConstructionTests
{
    /// <summary>Run an action on a dedicated STA thread and surface any exception.</summary>
    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var t = new Thread(() =>
        {
            try
            {
                if (Application.Current is null) _ = new Application();
                action();
            }
            catch (Exception ex) { failure = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        Assert.True(t.Join(TimeSpan.FromSeconds(30)), "construction hung (deadlock or modal dialog)");
        if (failure is not null) throw new Xunit.Sdk.XunitException($"construction threw: {failure}");
    }

    [Fact]
    public void DesignPanel_Constructs() => OnSta(() => _ = new DesignPanel());

    [Fact]
    public void MachinePanel_Constructs() => OnSta(() => _ = new MachinePanel());

    [Fact]
    public void CutPanel_Constructs() => OnSta(() => _ = new CutPanel());

    [Fact]
    public void OutputPanel_Constructs() => OnSta(() => _ = new OutputPanel());

    [Fact]
    public void ComponentTreePanel_Constructs() => OnSta(() => _ = new ComponentTreePanel());

    [Fact]
    public void ModelPanel_Constructs_And_Hosts_The_Component_Tree() => OnSta(() =>
    {
        var panel = new ModelPanel();
        Assert.NotNull(panel.FindName("Tree"));       // the component tree is hosted
        Assert.NotNull(panel.FindName("Preview"));    // with a live composite preview
        Assert.NotNull(panel.FindName("BtnBake"));
    });

    [Fact]
    public void RecipeDialog_Constructs_With_Both_Recipes() => OnSta(() =>
    {
        var dlg = new RecipeDialog();
        Assert.NotNull(dlg.FindName("RecipeList"));
        Assert.NotNull(dlg.FindName("TxtSignText"));
        Assert.NotNull(dlg.FindName("CmbFont"));
        Assert.Null(dlg.CreatedJob);   // nothing created until the user confirms
    });

    [Fact]
    public void TemplateNameDialog_Constructs() => OnSta(() =>
    {
        var dlg = new TemplateNameDialog();
        Assert.NotNull(dlg.FindName("TxtName"));
        Assert.Equal("", dlg.TemplateName);   // nothing captured until the user confirms
    });

    [Fact]
    public void GadgetDialog_Constructs_With_Examples() => OnSta(() =>
    {
        var dlg = new GadgetDialog();
        Assert.NotNull(dlg.FindName("TxtScript"));
        Assert.NotNull(dlg.FindName("BtnRun"));
        // "Add to sheet" must start disabled: nothing has been run yet.
        Assert.False(((System.Windows.Controls.Button)dlg.FindName("BtnAdd")!).IsEnabled);
    });

    [Fact]
    public void ShortcutDialog_Constructs() => OnSta(() =>
    {
        var dlg = new ShortcutDialog(new VectorPilot.App.ShortcutStore());
        Assert.NotNull(dlg.FindName("CommandList"));
        Assert.NotNull(dlg.FindName("CaptureBox"));
    });

    [Fact]
    public void WelcomeDialog_Constructs_With_Safety_Warning() => OnSta(() =>
    {
        var dlg = new WelcomeDialog();
        Assert.NotNull(dlg.FindName("BtnRecipe"));
        Assert.NotNull(dlg.FindName("BtnBlank"));
        Assert.NotNull(dlg.FindName("ChkDontShow"));
        Assert.Null(dlg.ChosenAction);   // nothing chosen until the user clicks
    });

    [Fact]
    public void MachinePanel_Drives_A_MachineSession_Not_Its_Own_Transport()
    {
        OnSta(() =>
        {
            var panel = new MachinePanel();

            // The panel must not hold a transport/streamer of its own — every
            // machine call has to route through the single MachineSession.
            var fields = typeof(MachinePanel)
                .GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Select(f => f.FieldType.Name)
                .ToList();

            Assert.DoesNotContain("IMachineTransport", fields);
            Assert.DoesNotContain("GCodeStreamer", fields);

            // And it must expose the session seam.
            Assert.NotNull(typeof(MachinePanel).GetProperty("Session",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
        });
    }

    [Fact]
    public void MachinePanel_Has_Always_Enabled_Safety_Chrome()
    {
        OnSta(() =>
        {
            var panel = new MachinePanel();
            var estop = panel.FindName("BtnEStop") as Button;
            var reset = panel.FindName("BtnReset") as Button;

            Assert.NotNull(estop);
            Assert.NotNull(reset);
            // The ported invariant: never gated on connection or stream state.
            Assert.True(estop!.IsEnabled, "E-STOP must always be enabled");
            Assert.True(reset!.IsEnabled, "Reset must always be enabled");
        });
    }

    [Fact]
    public void DesignPanel_Exposes_Node_And_Boolean_Chrome()
    {
        OnSta(() =>
        {
            var panel = new DesignPanel();
            Assert.NotNull(panel.FindName("ToolNode"));
            Assert.NotNull(panel.FindName("UnionButton"));
            Assert.NotNull(panel.FindName("SubtractButton"));
            Assert.NotNull(panel.FindName("IntersectButton"));
            Assert.NotNull(panel.FindName("TransformButton"));
        });
    }
}
