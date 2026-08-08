using System.Globalization;
using System.Text;
using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>Minimal EPS (PostScript) exporter that round-trips through EpsImporter.</summary>
public static class EpsExporter
{
    public static string EpsString(IEnumerable<VectorShape> shapes)
    {
        var sb = new StringBuilder();
        sb.AppendLine("%!PS-Adobe-3.0 EPSF-3.0");
        sb.AppendLine("%%BoundingBox: 0 0 612 792");
        sb.AppendLine("newpath");
        foreach (var shape in shapes)
        {
            if (shape.Points.Count < 2) continue;
            var pts = shape.Closed && shape.Points.Count > 1 && shape.Points[0] == shape.Points[^1]
                ? shape.Points.Take(shape.Points.Count - 1).ToList()
                : shape.Points;
            sb.AppendLine($"{F(pts[0].X)} {F(pts[0].Y)} moveto");
            for (int i = 1; i < pts.Count; i++)
            {
                sb.AppendLine($"{F(pts[i].X)} {F(pts[i].Y)} lineto");
            }
            if (shape.Closed) sb.AppendLine("closepath");
        }
        sb.AppendLine("stroke");
        sb.AppendLine("showpage");
        return sb.ToString();
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>Minimal PDF exporter with a vector content stream (round-trips through PdfImporter).</summary>
public static class PdfExporter
{
    public static string PdfString(IEnumerable<VectorShape> shapes)
    {
        var shapesList = shapes.ToList();
        var content = new StringBuilder();
        content.AppendLine("q");
        foreach (var shape in shapesList)
        {
            if (shape.Points.Count < 2) continue;
            var pts = shape.Closed && shape.Points.Count > 1 && shape.Points[0] == shape.Points[^1]
                ? shape.Points.Take(shape.Points.Count - 1).ToList()
                : shape.Points;
            content.AppendLine($"{F(pts[0].X)} {F(pts[0].Y)} m");
            for (int i = 1; i < pts.Count; i++)
            {
                content.AppendLine($"{F(pts[i].X)} {F(pts[i].Y)} l");
            }
            content.AppendLine(shape.Closed ? "h f" : "S");
        }
        content.AppendLine("Q");

        var stream = content.ToString();
        var objects = new StringBuilder();
        objects.AppendLine("%PDF-1.4");
        objects.AppendLine("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj");
        objects.AppendLine("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj");
        objects.AppendLine("3 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R >>\nendobj");
        objects.AppendLine($"4 0 obj\n<< /Length {stream.Length} >>\nstream\n{stream}endstream\nendobj");

        var text = objects.ToString();
        int xrefOffset = text.Length; // ASCII: chars == bytes

        // Byte offsets of each object header.
        var offsets = new List<int>();
        for (int i = 1; i <= 4; i++)
        {
            offsets.Add(text.IndexOf($"{i} 0 obj", StringComparison.Ordinal));
        }

        var sb = new StringBuilder(text);
        sb.Append($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (var off in offsets)
        {
            sb.Append($"{off:0000000000} 00000 n \n");
        }
        sb.Append($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");
        return sb.ToString();
    }

    private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
