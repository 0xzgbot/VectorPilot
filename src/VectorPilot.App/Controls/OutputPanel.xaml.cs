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
        var path = TapExporter.Export(Path.Combine(OutputDir(), Sanitize(AppState.CurrentJob.Name) + ".tap"), toolpaths);
        TxtExportInfo.Text = $"Wrote {path}";
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

    private void JobSheetPdf_Click(object sender, RoutedEventArgs e)
    {
        // The PDF renderer lands via the JobSheetPdfRenderer port (delegation wave);
        // guard on its presence so the button degrades gracefully if absent.
        var rendererType = typeof(JobSheetData).Assembly.GetType("VectorPilot.Engine.JobSheetPdfRenderer")
                          ?? typeof(JobSheetData).Assembly.GetType("VectorPilot.Engine.Post.JobSheetPdfRenderer");
        if (rendererType is null || rendererType.GetMethod("RenderPdf") is null)
        {
            MessageBox.Show("PDF job-sheet renderer is not available yet — use the HTML job sheet.", "Job sheet", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var data = BuildJobSheetData();
        var pdf = rendererType.GetMethod("RenderPdf")!.Invoke(null, new object[] { data }) as byte[];
        if (pdf is null) return;
        var path = Path.Combine(OutputDir(), Sanitize(AppState.CurrentJob.Name) + "-jobsheet.pdf");
        File.WriteAllBytes(path, pdf);
        TxtExportInfo.Text = $"Wrote {path}";
    }
}
