using System.Globalization;
using System.Text;

namespace VectorPilot.Engine;

/// <summary>
/// ASCII Wavefront OBJ → heightfield importer (ported from OBJHeightfield.swift).
/// Tolerant parsing of v/f records; faces fan-triangulated; rasterized like STL.
/// </summary>
public static class ObjImporter
{
    public static HeightfieldImportResult Import(byte[] data, double cellSizeMm = 1.0, double scale = 1.0)
    {
        if (data.Any(b => b == 0))
        {
            return new HeightfieldImportResult { Success = false, FileSizeBytes = data.Length, ErrorMessage = "Binary input is not a supported OBJ — export ASCII Wavefront OBJ" };
        }
        var text = Encoding.UTF8.GetString(data);
        var (triangles, vertexCount, faceCount) = ParseAscii(text);
        if (triangles.Count == 0)
        {
            return new HeightfieldImportResult
            {
                Success = false, FileSizeBytes = data.Length, VertexCount = vertexCount, FaceCount = faceCount,
                ErrorMessage = $"OBJ contains no valid faces ({faceCount} faces / {vertexCount} vertices parsed)"
            };
        }
        var grid = StlImporter.Rasterize(triangles, cellSizeMm, scale);
        return new HeightfieldImportResult
        {
            Heightfield = grid, TriangleCount = triangles.Count, VertexCount = vertexCount,
            FaceCount = faceCount, FileSizeBytes = data.Length, Success = true
        };
    }

    public static (List<StlImporter.Triangle> Triangles, int VertexCount, int FaceCount) ParseAscii(string text)
    {
        var verts = new List<(double X, double Y, double Z)>();
        var triangles = new List<StlImporter.Triangle>();
        int faceCount = 0;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var tokens = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            switch (tokens[0])
            {
                case "v" when tokens.Length >= 4:
                    if (TryNum(tokens[1], out var x) && TryNum(tokens[2], out var y) && TryNum(tokens[3], out var z))
                    {
                        verts.Add((x, y, z));
                    }
                    break;
                case "f" when tokens.Length >= 4:
                {
                    var indices = new List<int>();
                    bool valid = true;
                    for (int i = 1; i < tokens.Length; i++)
                    {
                        var comp = tokens[i].Split('/')[0];
                        if (!int.TryParse(comp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw))
                        {
                            valid = false;
                            break;
                        }
                        int resolved = raw > 0 ? raw - 1 : raw < 0 ? verts.Count + raw : -1;
                        if (resolved < 0 || resolved >= verts.Count)
                        {
                            valid = false;
                            break;
                        }
                        indices.Add(resolved);
                    }
                    if (!valid || indices.Count < 3) break;
                    faceCount++;
                    for (int i = 1; i < indices.Count - 1; i++)
                    {
                        var a = verts[indices[0]];
                        var b = verts[indices[i]];
                        var c = verts[indices[i + 1]];
                        var ab = (b.X - a.X, b.Y - a.Y, b.Z - a.Z);
                        var ac = (c.X - a.X, c.Y - a.Y, c.Z - a.Z);
                        double nx = ab.Item2 * ac.Item3 - ab.Item3 * ac.Item2;
                        double ny = ab.Item3 * ac.Item1 - ab.Item1 * ac.Item3;
                        double nz = ab.Item1 * ac.Item2 - ab.Item2 * ac.Item1;
                        if (Math.Abs(nx) + Math.Abs(ny) + Math.Abs(nz) > 1e-12)
                        {
                            triangles.Add(new StlImporter.Triangle(a, b, c));
                        }
                    }
                    break;
                }
            }
        }
        return (triangles, verts.Count, faceCount);
    }

    private static bool TryNum(string s, out double v)
        => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
}
