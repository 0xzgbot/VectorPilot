using System.IO;
using System.Windows;
using System.Windows.Controls;
using VectorPilot.Engine;

namespace VectorPilot.App.Controls;

public partial class OutputPanel : UserControl
{
    public OutputPanel()
    {
        InitializeComponent();
        Loaded += (_, _) => Refresh();
    }

    public void Refresh()
    {
        var toolpaths = AppState.Toolpaths.Toolpaths;
        ListToolpaths.ItemsSource = toolpaths.Select(t => $"{t.Name} — {t.Strategy} ({(t.GCode.Count > 0 ? $"{t.GCode.Count} lines" : "not calculated")})").ToList();
        CmbPost.ItemsSource = PostTemplate.Shipped;
        CmbPost.DisplayMemberPath = "Name";
        CmbPost.SelectedIndex = 0;

        // Card E4: whole-job estimate with cut/travel split and tool changes.
        var est = JobTimeEstimator.Estimate(toolpaths);
        TxtTimeEstimate.Text = est.TotalSeconds <= 0
            ? ""
            : $"Est. {est.Formatted}  (cut {TimeSpan.FromSeconds(est.CuttingSeconds):mm\\:ss} · " +
              $"travel {TimeSpan.FromSeconds(est.RapidSeconds):mm\\:ss}" +
              (est.ToolChanges > 0 ? $" · {est.ToolChanges} tool change(s)" : "") + ")";
        TxtExportInfo.Text = $"{toolpaths.Count} toolpath(s) ready";
    }

    private static string OutputDir()
    {
        var job = AppState.CurrentJob;
        if (!string.IsNullOrEmpty(job.FilePath) && Directory.Exists(Path.GetDirectoryName(job.FilePath)))
        {
            return Path.GetDirectoryName(job.FilePath)!;
        }
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Directory.CreateDirectory(docs);
        return docs;
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    private void ExportTap_Click(object sender, RoutedEventArgs e)
    {
        var toolpaths = AppState.Toolpaths.Toolpaths.Where(t => t.GCode.Count > 0).ToList();
        if (toolpaths.Count == 0)
        {
            MessageBox.Show("Nothing to export — calculate toolpaths in the Cut stage first.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Honour the post picker. This used to call the plain exporter and ignore
        // CmbPost entirely, so the selected controller had no effect on the .tap —
        // and it overwrote the same filename the template export writes.
        string target = Path.Combine(OutputDir(), Sanitize(AppState.CurrentJob.Name) + ".tap");

        if (CmbPost.SelectedItem is PostTemplate template)
        {
            var written = TapExporter.ExportWithTemplate(target, toolpaths, template);
            TxtExportInfo.Text = $"Wrote {written} (post: {template.Name})";
            return;
        }

        var path = TapExporter.Export(target, toolpaths);
        TxtExportInfo.Text = $"Wrote {path} (no post selected — generic GRBL)";
    }

    private void ExportTemplate_Click(object sender, RoutedEventArgs e)
    {
        var toolpaths = AppState.Toolpaths.Toolpaths.Where(t => t.GCode.Count > 0).ToList();
        if (toolpaths.Count == 0)
        {
            MessageBox.Show("Nothing to export — calculate toolpaths in the Cut stage first.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var template = CmbPost.SelectedItem as PostTemplate;
        if (template is null) return;
        var path = TapExporter.ExportWithTemplate(Path.Combine(OutputDir(), Sanitize(AppState.CurrentJob.Name) + ".tap"), toolpaths, template);
        TxtExportInfo.Text = $"Wrote {path} (template: {template.Name})";
    }

    private void JobSheetHtml_Click(object sender, RoutedEventArgs e)
    {
        var data = BuildJobSheetData();
        var html = JobSheetHTMLTemplateEngine.Fill(data);
        var path = Path.Combine(OutputDir(), Sanitize(AppState.CurrentJob.Name) + "-jobsheet.html");
        File.WriteAllText(path, html);
        TxtExportInfo.Text = $"Wrote {path}";
    }

    private static JobSheetData BuildJobSheetData()
    {
        var job = AppState.CurrentJob;
        var sheet = job.Sheets.FirstOrDefault();
        return new JobSheetData
        {
            JobName = job.Name,
            Material = sheet?.Material?.Name ?? "—",
            SheetWidth = sheet?.Width ?? 0,
            SheetHeight = sheet?.Height ?? 0,
            CreatedAt = DateTime.Now,
            Toolpaths = AppState.Toolpaths.Toolpaths.Select(t => new ToolpathInfo
            {
                Name = t.Name,
                Type = ToSheetType(t.Strategy),
                Tool = t.ToolId != Guid.Empty ? t.ToolId.ToString()[..8] : "—",
                FeedRate = t.FeedRate,
                Depth = t.CutDepth,
                EstimatedTime = t.EstimatedTimeSeconds
            }).ToList()
        };
    }

    private static JobSheetToolpathType ToSheetType(ToolpathStrategy s) => s switch
    {
        ToolpathStrategy.Pocket => JobSheetToolpathType.Pocket,
        ToolpathStrategy.Drill => JobSheetToolpathType.Drill,
        ToolpathStrategy.VCarve => JobSheetToolpathType.VCarve,
        ToolpathStrategy.QuickEngrave => JobSheetToolpathType.QuickEngrave,
        _ => JobSheetToolpathType.Profile
    };

    /// <summary>Card P4: simulate material removal for the whole job.</summary>
    private void Simulate_Click(object sender, RoutedEventArgs e)
    {
        var gcode = AppState.Toolpaths.Toolpaths.SelectMany(t => t.GCode).ToList();
        if (gcode.Count == 0)
        {
            TxtExportInfo.Text = "Calculate toolpaths first — nothing to simulate";
            return;
        }

        var sheet = AppState.CurrentJob.ActiveSheet;
        double w = Dim(sheet.Width, 200), h = Dim(sheet.Height, 200), t = Dim(sheet.Thickness, 19.05);

        var r = MaterialSimulator.Simulate(gcode, w, h, t, cellSizeMm: 1.0);

        TxtExportInfo.Text =
            $"removed {r.RemovedVolumeMm3 / 1000.0:F1} cm³ · " +
            $"coverage {r.CoverageFraction:P0} · " +
            $"deepest {r.MaxCutDepthMm:F2} mm" +
            (r.CutThrough ? "  ⚠ CUTS THROUGH THE STOCK" : "");

        if (r.CutThrough && !App.IsAutomated)
        {
            MessageBox.Show(
                $"The program cuts {r.MaxCutDepthMm:F2} mm deep into {t:F2} mm stock.\n\n" +
                "Check cut depths, or add a spoilboard allowance.",
                "Cut-through warning", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static double Dim(object? value, double fallback) => value switch
    {
        double d => d,
        string s when double.TryParse(s, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var p) => p,
        _ => fallback
    };

    private void JobSheetPdf_Click(object sender, RoutedEventArgs e)
    {
        // Direct call: JobSheetPdfRenderer.RenderPdf ships in VectorPilot.Engine. The
        // old reflection probe plus "not available yet" dialog meant this button told
        // the user the feature was missing while the renderer sat right there.
        try
        {
            var pdf = JobSheetPdfRenderer.RenderPdf(BuildJobSheetData());
            if (pdf.Length == 0)
            {
                TxtExportInfo.Text = "Job sheet PDF came back empty — nothing written.";
                return;
            }

            var path = Path.Combine(OutputDir(), Sanitize(AppState.CurrentJob.Name) + "-jobsheet.pdf");
            File.WriteAllBytes(path, pdf);
            TxtExportInfo.Text = $"Wrote {path} ({pdf.Length:N0} bytes)";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TxtExportInfo.Text = $"Could not write the job sheet: {ex.Message}";
        }
    }
}
