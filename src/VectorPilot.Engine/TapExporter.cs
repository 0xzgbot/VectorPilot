using System.Text;
using VectorPilot.Serial;

namespace VectorPilot.Engine;

/// <summary>
/// .tap output (output-panel parity): writes processed G-code as a .tap file
/// with a header/footer and per-toolpath comments. The post processor is
/// applied first (GRBL by default).
/// </summary>
public static class TapExporter
{
    /// <summary>SPK-0603 dirty-toolpath export gate: skips toolpaths that are
    /// dirty (edited since last calculation) and reports them as warnings.
    /// Clean toolpaths export normally.</summary>
    public static (string Path, List<string> Warnings) ExportWithGate(string outputPath, IReadOnlyList<Toolpath> toolpaths, PostProcessorType post = PostProcessorType.Grbl)
    {
        var warnings = new List<string>();
        var clean = toolpaths.Where(tp =>
        {
            if (tp.IsDirty)
            {
                warnings.Add($"{tp.Name}: dirty (recalculate before export) — skipped");
                return false;
            }
            return true;
        }).ToList();
        if (clean.Count == 0)
        {
            throw new InvalidOperationException("All toolpaths are dirty — nothing to export. Recalculate first.");
        }
        return (Export(outputPath, clean, post), warnings);
    }

    /// <summary>Write toolpaths to a .tap file; returns the file path.</summary>
    public static string Export(string outputPath, IReadOnlyList<Toolpath> toolpaths, PostProcessorType post = PostProcessorType.Grbl)
    {
        var pp = post == PostProcessorType.Grbl ? GRBLPostProcessor.Grbl() : GRBLPostProcessor.Universal();
        var sb = new StringBuilder();
        sb.AppendLine($"(VectorPilot job export)");
        sb.AppendLine($"(Exported {DateTime.Now:yyyy-MM-dd HH:mm})");

        foreach (var tp in toolpaths)
        {
            sb.AppendLine($"");
            sb.AppendLine($"(Toolpath: {tp.Name} — {tp.Strategy})");
            if (tp.EstimatedTimeSeconds > 0)
            {
                sb.AppendLine($"(Est. time: {TimeSpan.FromSeconds(tp.EstimatedTimeSeconds):hh\\:mm\\:ss})");
            }
            var processed = pp.Process(tp.GCode, tp.ToolId != Guid.Empty ? tp.ToolId.ToString() : null);
            sb.AppendLine(processed.GcodeString);
        }

        File.WriteAllText(outputPath, sb.ToString());
        return outputPath;
    }

    /// <summary>SPK-1134: export through the post template engine (rotary wrap,
    /// GRBL mm/in, line numbers) instead of the legacy wrapper.</summary>
    public static string ExportWithTemplate(string outputPath, IReadOnlyList<Toolpath> toolpaths, PostTemplate template)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"(VectorPilot job export)");
        sb.AppendLine($"(Exported {DateTime.Now:yyyy-MM-dd HH:mm})");

        foreach (var tp in toolpaths)
        {
            sb.AppendLine($"");
            sb.AppendLine($"(Toolpath: {tp.Name} — {tp.Strategy})");
            var result = PostTemplateEngine.Emit(tp.GCode, template);
            sb.AppendLine(string.Join("\n", result.Lines));
        }

        File.WriteAllText(outputPath, sb.ToString());
        return outputPath;
    }

    /// <summary>Default export path next to the job file.</summary>
    public static string DefaultPath(string jobPath) => Path.ChangeExtension(jobPath, ".tap");
}

/// <summary>One job-sheet row (Aspire job-sheet parity).</summary>
public sealed class JobSheetRow
{
    public string Name { get; init; } = "";
    public string Strategy { get; init; } = "";
    public string Tool { get; init; } = "";
    public double EstimatedSeconds { get; init; }
    public int LineCount { get; init; }
}

/// <summary>
/// HTML job sheet (Aspire PrintSheetTemplate.html parity): renders job info +
/// a toolpath table from an HTML template with {TOKEN} placeholders.
/// </summary>
public static class JobSheetHtml
{
    public const string DefaultTemplate = """
        <!DOCTYPE html>
        <html>
        <head><meta charset="utf-8"><title>VectorPilot Job Sheet</title>
        <style>
        body { font-family: sans-serif; margin: 24px; }
        h1 { font-size: 18px; } h2 { font-size: 14px; margin-top: 24px; }
        table { border-collapse: collapse; width: 100%; }
        th, td { border: 1px solid #999; padding: 6px 10px; text-align: left; font-size: 13px; }
        th { background: #eee; }
        .muted { color: #666; font-size: 12px; }
        </style></head>
        <body>
        <h1>{JOB_NAME}</h1>
        <p class="muted">{JOB_PATH}</p>
        <h2>Job</h2>
        <table>
        <tr><th>Sheet (mm)</th><th>Thickness (mm)</th><th>Material</th><th>Units</th><th>Exported</th></tr>
        <tr><td>{SHEET_SIZE}</td><td>{SHEET_THICKNESS}</td><td>{MATERIAL}</td><td>{UNITS}</td><td>{EXPORTED}</td></tr>
        </table>
        <h2>Toolpaths ({TOOLPATH_COUNT})</h2>
        <table>
        <tr><th>Name</th><th>Strategy</th><th>Tool</th><th>Est. Time</th><th>Lines</th></tr>
        {TOOLPATH_ROWS}
        </table>
        </body>
        </html>
        """;

    /// <summary>Render the job sheet HTML with the given rows.</summary>
    public static string Render(string jobName, string jobPath, double sheetWidth, double sheetDepth, double thickness, string material, string units, IReadOnlyList<JobSheetRow> rows)
    {
        var rowsHtml = new StringBuilder();
        foreach (var r in rows)
        {
            rowsHtml.AppendLine(
                $"<tr><td>{Html(r.Name)}</td><td>{Html(r.Strategy)}</td><td>{Html(r.Tool)}</td>" +
                $"<td>{TimeSpan.FromSeconds(r.EstimatedSeconds):hh\\:mm\\:ss}</td><td>{r.LineCount}</td></tr>");
        }

        return DefaultTemplate
            .Replace("{JOB_NAME}", Html(jobName))
            .Replace("{JOB_PATH}", Html(jobPath))
            .Replace("{SHEET_SIZE}", $"{sheetWidth:0.##} × {sheetDepth:0.##}")
            .Replace("{SHEET_THICKNESS}", $"{thickness:0.##}")
            .Replace("{MATERIAL}", Html(material))
            .Replace("{UNITS}", Html(units))
            .Replace("{EXPORTED}", DateTime.Now.ToString("yyyy-MM-dd HH:mm"))
            .Replace("{TOOLPATH_COUNT}", rows.Count.ToString())
            .Replace("{TOOLPATH_ROWS}", rowsHtml.ToString());
    }

    public static string RenderToFile(string outputPath, string jobName, string jobPath, double sheetWidth, double sheetDepth, double thickness, string material, string units, IReadOnlyList<JobSheetRow> rows)
    {
        var html = Render(jobName, jobPath, sheetWidth, sheetDepth, thickness, material, units, rows);
        File.WriteAllText(outputPath, html);
        return outputPath;
    }

    private static string Html(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
