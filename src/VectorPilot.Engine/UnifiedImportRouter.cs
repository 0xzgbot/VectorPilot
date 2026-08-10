using System.IO;
using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Unified import router (ported from UnifiedImportRouter.swift, SPK-0216):
/// one entry point for every vector format; dispatches by extension and
/// surfaces warnings instead of throwing.
/// </summary>
public static class UnifiedImportRouter
{
    public enum Format
    {
        Svg, Dxf, Eps, Pdf, Ai, Dwg
    }

    public static class FormatInfo
    {
        public static IReadOnlyList<string> Extensions(Format f) => f switch
        {
            Format.Svg => new[] { "svg" },
            Format.Dxf => new[] { "dxf" },
            Format.Eps => new[] { "eps" },
            Format.Pdf => new[] { "pdf" },
            Format.Ai => new[] { "ai" },
            _ => new[] { "dwg" }
        };

        public static string DisplayName(Format f) => f switch
        {
            Format.Svg => "SVG", Format.Dxf => "DXF", Format.Eps => "EPS",
            Format.Pdf => "PDF", Format.Ai => "AI", _ => "DWG"
        };

        public static Format? FromExtension(string ext)
        {
            var lower = ext.ToLowerInvariant();
            foreach (var f in System.Enum.GetValues<Format>())
            {
                if (Extensions(f).Contains(lower)) return f;
            }
            return null;
        }
    }

    public sealed class Result
    {
        public Format Format { get; init; }
        public List<VectorShape> Shapes { get; init; } = new();
        public List<string> Warnings { get; init; } = new();
    }

    public static Result ImportFile(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.');
        var format = FormatInfo.FromExtension(ext);
        if (format is null)
        {
            return new Result { Format = Format.Svg, Warnings = { $"Unsupported file extension: {ext}" } };
        }
        return ImportFile(path, format.Value);
    }

    public static Result ImportFile(string path, Format format)
    {
        switch (format)
        {
            case Format.Svg:
            {
                if (!TryReadText(path, out var content, out var warn)) return Warn(format, warn);
                return new Result { Format = format, Shapes = SvgImporter.Parse(content) };
            }
            case Format.Dxf:
            {
                if (!TryReadText(path, out var content, out var warn)) return Warn(format, warn);
                var shapes = DxfImporter.Parse(content);
                return new Result { Format = format, Shapes = shapes, Warnings = shapes.Count == 0 ? new List<string> { "No entities found" } : new List<string>() };
            }
            case Format.Eps:
            {
                if (!TryReadText(path, out var content, out var warn)) return Warn(format, warn);
                return new Result { Format = format, Shapes = EpsImporter.Parse(content) };
            }
            case Format.Pdf:
            {
                if (!TryReadText(path, out var content, out var warn)) return Warn(format, warn);
                return new Result { Format = format, Shapes = PdfImporter.Parse(content) };
            }
            case Format.Ai:
            {
                if (!TryReadText(path, out var content, out var warn)) return Warn(format, warn);
                return new Result { Format = format, Shapes = AiImporter.Parse(content) };
            }
            default:
            {
                try
                {
                    return new Result { Format = format, Shapes = DwgImporter.Parse(File.ReadAllBytes(path)) };
                }
                catch (Exception ex)
                {
                    return new Result { Format = format, Warnings = { $"DWG import failed: {ex.Message}" } };
                }
            }
        }
    }

    private static bool TryReadText(string path, out string content, out string warning)
    {
        content = "";
        warning = "";
        try
        {
            content = File.ReadAllText(path);
            return true;
        }
        catch (Exception ex)
        {
            warning = $"Failed to read file: {ex.Message}";
            return false;
        }
    }

    private static Result Warn(Format f, string warning)
        => new() { Format = f, Warnings = { warning } };
}
