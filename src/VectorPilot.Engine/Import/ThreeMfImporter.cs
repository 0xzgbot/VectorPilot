using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace VectorPilot.Engine;

/// <summary>
/// 3MF (ZIP + XML) heightfield importer (ported from ThreeMFImporter.swift).
/// Extracts the model XML, parses vertex/triangle records, rasterizes like STL.
/// </summary>
public static class ThreeMfImporter
{
    public static HeightfieldImportResult Import(byte[] data, double cellSizeMm = 1.0, double scale = 1.0)
    {
        try
        {
            string? xml = null;
            using (var ms = new MemoryStream(data))
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                var entry = zip.Entries.FirstOrDefault(e => e.Name.Equals("3dmodel.model", StringComparison.OrdinalIgnoreCase));
                if (entry is null)
                {
                    return new HeightfieldImportResult { Success = false, FileSizeBytes = data.Length, ErrorMessage = "3MF archive has no 3dmodel.model entry" };
                }
                using var reader = new StreamReader(entry.Open());
                xml = reader.ReadToEnd();
            }

            var (vertices, refs) = ParseModelXml(xml);
            var triangles = new List<StlImporter.Triangle>();
            foreach (var (v1, v2, v3) in refs)
            {
                if (v1 < 0 || v2 < 0 || v3 < 0 || v1 >= vertices.Count || v2 >= vertices.Count || v3 >= vertices.Count) continue;
                triangles.Add(new StlImporter.Triangle(vertices[v1], vertices[v2], vertices[v3]));
            }
            if (triangles.Count == 0)
            {
                return new HeightfieldImportResult
                {
                    Success = false, FileSizeBytes = data.Length, VertexCount = vertices.Count,
                    ErrorMessage = $"3MF model contains no valid triangles ({refs.Count} refs / {vertices.Count} vertices)"
                };
            }
            var grid = StlImporter.Rasterize(triangles, cellSizeMm, scale);
            return new HeightfieldImportResult
            {
                Heightfield = grid, TriangleCount = triangles.Count, VertexCount = vertices.Count,
                FaceCount = refs.Count, FileSizeBytes = data.Length, Success = true
            };
        }
        catch (InvalidDataException ex)
        {
            return new HeightfieldImportResult { Success = false, FileSizeBytes = data.Length, ErrorMessage = $"Not a valid 3MF archive (ZIP): {ex.Message}" };
        }
        catch (Exception ex)
        {
            return new HeightfieldImportResult { Success = false, FileSizeBytes = data.Length, ErrorMessage = $"3MF unreadable: {ex.Message}" };
        }
    }

    public static (List<(double X, double Y, double Z)> Vertices, List<(int V1, int V2, int V3)> TriangleRefs) ParseModelXml(string xml)
    {
        var vertices = new List<(double, double, double)>();
        var refs = new List<(int, int, int)>();
        var doc = XDocument.Parse(xml);
        var ns = doc.Root?.Name.Namespace ?? XNamespace.None;
        foreach (var v in doc.Descendants(ns + "vertex"))
        {
            if (TryAttr(v, "x", out var x) && TryAttr(v, "y", out var y) && TryAttr(v, "z", out var z))
            {
                vertices.Add((x, y, z));
            }
        }
        foreach (var t in doc.Descendants(ns + "triangle"))
        {
            if (TryAttrInt(t, "v1", out var v1) && TryAttrInt(t, "v2", out var v2) && TryAttrInt(t, "v3", out var v3))
            {
                refs.Add((v1, v2, v3));
            }
        }
        return (vertices, refs);
    }

    private static bool TryAttr(XElement el, string name, out double v)
        => double.TryParse(el.Attribute(name)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out v);

    private static bool TryAttrInt(XElement el, string name, out int v)
        => int.TryParse(el.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out v);
}
