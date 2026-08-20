using System.Windows;
using System.Windows.Controls;
using VectorPilot.Engine;
using VectorPilot.Geometry;

namespace VectorPilot.App.Controls;

/// <summary>
/// Lua gadget host UI (Aspire gadget row). Edit a script, run it in the sandbox, see
/// what it drew, then commit the shapes to the active layer.
/// </summary>
public partial class GadgetDialog : Window
{
    /// <summary>Shipped examples — each demonstrates one part of the vp API.</summary>
    private static readonly (string Name, string Script)[] Examples =
    {
        ("Bolt circle", """
            -- Evenly spaced holes on a bolt circle.
            local n, r = 8, 40
            for i = 0, n - 1 do
              local a = i / n * 2 * math.pi
              vp.circle(100 + math.cos(a) * r, 100 + math.sin(a) * r, 4)
            end
            vp.log(n .. " holes on a " .. (r * 2) .. "mm circle")
            """),

        ("Dovetail comb", """
            -- Dovetail pin row across the sheet width.
            local w = vp.sheet_width()
            if w <= 0 then w = 300 end
            local pitch, depth = 25, 18
            local x = 10
            while x + pitch <= w - 10 do
              vp.polyline({{x, 0}, {x + 6, depth}, {x + pitch - 6, depth}, {x + pitch, 0}}, false)
              x = x + pitch
            end
            """),

        ("Spiral", """
            -- Archimedean spiral as a single polyline.
            local pts, turns = {}, 4
            for i = 0, 360 * turns, 5 do
              local a = math.rad(i)
              local r = i / (360 * turns) * 60
              pts[#pts + 1] = {100 + math.cos(a) * r, 100 + math.sin(a) * r}
            end
            vp.polyline(pts, false)
            """),

        ("Nested frames", """
            -- Concentric rectangles, 10mm apart.
            for i = 0, 5 do
              local m = i * 10
              vp.rect(20 + m, 20 + m, 160 - m * 2, 120 - m * 2)
            end
            """)
    };

    private List<VectorShape> _shapes = new();

    public GadgetDialog()
    {
        InitializeComponent();
        CmbExample.ItemsSource = Examples.Select(e => e.Name).ToList();
        CmbExample.SelectedIndex = 0;
    }

    private void Example_Changed(object sender, SelectionChangedEventArgs e)
    {
        int i = CmbExample.SelectedIndex;
        if (i >= 0 && i < Examples.Length) TxtScript.Text = Examples[i].Script;
    }

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        var sheet = AppState.CurrentJob?.ActiveSheet;
        var result = LuaGadgetHost.Run(TxtScript.Text, sheet?.Width ?? 0, sheet?.Height ?? 0);

        _shapes = result.Shapes;
        BtnAdd.IsEnabled = result.Ok;

        TxtResult.Text = result.Ok
            ? $"Drew {result.Shapes.Count} shape(s)."
                + (result.Log.Count > 0 ? "  " + string.Join(" · ", result.Log) : "")
            : result.Error;
        TxtResult.Foreground = result.Ok
            ? System.Windows.Media.Brushes.SeaGreen
            : System.Windows.Media.Brushes.Firebrick;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var layer = AppState.CurrentJob?.ActiveSheet.ActiveLayer;
        if (layer is null || _shapes.Count == 0) return;

        foreach (var s in _shapes) layer.AddShape(s);
        DialogResult = true;
    }
}