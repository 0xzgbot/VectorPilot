using System.Globalization;
using System.Text;

namespace VectorPilot.Engine;

/// <summary>STL/OBJ mesh exporters (ASCII + binary STL; ASCII OBJ).</summary>
public static class MeshExporter
{
    /// <summary>ASCII STL from triangles.</summary>
    public static string StlAscii(IEnumerable<StlImporter.Triangle> triangles, string solidName = "vectorpilot")
    {
        var sb = new StringBuilder();
        sb.AppendLine($"solid {solidName}");
        foreach (var t in triangles)
        {
            sb.AppendLine("  facet normal 0 0 0");
            sb.AppendLine("    outer loop");
            sb.AppendLine($"      vertex {F(t.A.X)} {F(t.A.Y)} {F(t.A.Z)}");
            sb.AppendLine($"      vertex {F(t.B.X)} {F(t.B.Y)} {F(t.B.Z)}");
            sb.AppendLine($"      vertex {F(t.C.X)} {F(t.C.Y)} {F(t.C.Z)}");
            sb.AppendLine("    endloop");
            sb.AppendLine("  endfacet");
        }
        sb.AppendLine($"endsolid {solidName}");
        return sb.ToString();
    }

    /// <summary>Binary STL from triangles.</summary>
    public static byte[] StlBinary(IEnumerable<StlImporter.Triangle> triangles)
    {
        var list = triangles.ToList();
        using var ms = new MemoryStream();
        ms.Write(new byte[80]);
        ms.Write(BitConverter.GetBytes((uint)list.Count));
        foreach (var t in list)
        {
            for (int i = 0; i < 3; i++) ms.Write(BitConverter.GetBytes(0f));
            WriteV(ms, t.A); WriteV(ms, t.B); WriteV(ms, t.C);
            ms.Write(BitConverter.GetBytes((ushort)0));
        }
        return ms.ToArray();
    }

    private static void WriteV(Stream s, (double X, double Y, double Z) v)
    {
        s.Write(BitConverter.GetBytes((float)v.X));
        s.Write(BitConverter.GetBytes((float)v.Y));
        s.Write(BitConverter.GetBytes((float)v.Z));
    }

    /// <summary>ASCII Wavefront OBJ from triangles.</summary>
    public static string ObjAscii(IEnumerable<StlImporter.Triangle> triangles)
    {
        var sb = new StringBuilder();
        var list = triangles.ToList();
        var verts = new List<(double, double, double)>();
        foreach (var t in list)
        {
            verts.Add(t.A); verts.Add(t.B); verts.Add(t.C);
        }
        foreach (var v in verts)
        {
            sb.AppendLine($"v {F(v.Item1)} {F(v.Item2)} {F(v.Item3)}");
        }
        for (int i = 0; i < list.Count; i++)
        {
            int baseIdx = i * 3;
            sb.AppendLine($"f {baseIdx + 1} {baseIdx + 2} {baseIdx + 3}");
        }
        return sb.ToString();
    }

    /// <summary>Triangulate a heightfield as a grid mesh (two triangles per cell).</summary>
    public static List<StlImporter.Triangle> HeightfieldToTriangles(HeightfieldData hf)
    {
        var tris = new List<StlImporter.Triangle>();
        for (int row = 0; row < hf.Height - 1; row++)
        {
            for (int col = 0; col < hf.Width - 1; col++)
            {
                double x0 = hf.MinX + col * hf.CellSizeMm;
                double x1 = x0 + hf.CellSizeMm;
                double y0 = hf.MinY + row * hf.CellSizeMm;
                double y1 = y0 + hf.CellSizeMm;
                double z00 = hf.Heights[row * hf.Width + col];
                double z10 = hf.Heights[row * hf.Width + col + 1];
                double z01 = hf.Heights[(row + 1) * hf.Width + col];
                double z11 = hf.Heights[(row + 1) * hf.Width + col + 1];
                if (z00 > 0 || z10 > 0 || z01 > 0 || z11 > 0)
                {
                    tris.Add(new StlImporter.Triangle((x0, y0, z00), (x1, y0, z10), (x1, y1, z11)));
                    tris.Add(new StlImporter.Triangle((x0, y0, z00), (x1, y1, z11), (x0, y1, z01)));
                }
            }
        }
        return tris;
    }

    private static string F(double v) => v.ToString("0.000000", CultureInfo.InvariantCulture);
}
