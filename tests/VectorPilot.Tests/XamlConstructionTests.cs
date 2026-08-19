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
/// </summary>
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
