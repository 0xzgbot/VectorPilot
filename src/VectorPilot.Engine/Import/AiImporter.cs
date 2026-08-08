using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Adobe Illustrator files are containers: classic AI = EPS (PostScript with a
/// <c>%!PS-Adobe</c> header + <c>%%BoundingBox</c>), modern AI = PDF with embedded
/// vector content streams (<c>%PDF</c>). This importer detects the flavor from
/// the magic prefix and dispatches to the corresponding engine, so one import
/// path covers both generations. Ported from ShopPilotGeometry.AIImporter (Swift).
/// </summary>
public static class AiImporter
{
    /// <summary>
    /// Parse an Illustrator file (EPS or PDF flavor) into shapes. Dispatches on
    /// the magic header; throws <see cref="FormatException"/> for unsupported input.
    /// </summary>
    public static List<VectorShape> Parse(string content)
    {
        if (content is not null && content.StartsWith("%PDF", StringComparison.Ordinal))
        {
            return PdfImporter.Parse(content);
        }
        if (content is not null && content.StartsWith("%!PS-Adobe", StringComparison.Ordinal))
        {
            return EpsImporter.Parse(content);
        }
        throw new FormatException("unsupported AI flavor: expected %PDF or %!PS-Adobe header");
    }
}
