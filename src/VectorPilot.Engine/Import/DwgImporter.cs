using System.Buffers.Binary;
using System.Text;
using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Minimal AutoCAD R12 (AC1009) binary DWG importer: reads the header's entity
/// section and converts LINE / POINT / CIRCLE / ARC entity records into
/// <see cref="VectorShape"/> values. Ported from ShopPilotGeometry.DWGImporter (Swift),
/// which itself is ported from the public <c>CAD::Format::DWG::AC1009</c> reference.
/// </summary>
/// <remarks>
/// R12 is the byte-structured DWG generation; post-R12 files are bit-coded and
/// unsupported here. Rejection mirrors the Swift behavior: a version mismatch or
/// corrupt section bounds yields an empty result rather than a throw (the Swift
/// side reports the failure on its result object; the DXF-export hint for
/// unsupported versions is documented in the Swift error strings). Malformed
/// entity records are skipped, and trailing sentinel/padding noise stops the
/// scan cleanly — parsing never throws.
/// </remarks>
public static class DwgImporter
{
    /// <summary>Parse R12 (AC1009) binary DWG data into shapes. Never throws.</summary>
    public static List<VectorShape> Parse(byte[] data)
    {
        var shapes = new List<VectorShape>();
        if (data is null || data.Length < 0x1C) return shapes;

        // Magic: 12 bytes ASCII, "AC1009" + null padding.
        var magic = Encoding.ASCII.GetString(data, 0, 12).TrimEnd('\0');
        if (magic != "AC1009") return shapes;

        // Header fields (all little-endian). entities_start/end are ABSOLUTE
        // file offsets of the entity records (the 16-byte sentinel precedes
        // entities_start; end points past the last record).
        var entitiesStart = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x14));
        var entitiesEnd = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x18));
        if (entitiesEnd > (uint)data.Length || entitiesEnd <= entitiesStart) return shapes;

        var cursor = (int)entitiesStart;
        var end = (int)entitiesEnd;

        while (cursor + 4 <= end)
        {
            var type = data[cursor];
            var modeByte = data[cursor + 1];
            var size = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(cursor + 2));
            if (size < 4 || cursor + size > end)
            {
                // Trailing padding / sentinel noise: stop cleanly.
                break;
            }
            var recordEnd = cursor + size;

            // R12 mode flags (bits MSB-first): 0x20 = has_handling,
            // 0x04 = has_elevation, 0x02 = has_linetype, 0x01 = has_color.
            // has_pspace(0x40)/has_attributes(0x80)/has_thickness(0x08) are
            // skipped records in this slice (extra_flag/eed structures).
            var exoticFlags = modeByte & 0xC8;
            if (exoticFlags != 0)
            {
                // Skip the whole record by entity_size (always safe).
                cursor = recordEnd;
                continue;
            }
            var hasElevation = (modeByte & 0x04) != 0;
            var hasColor = (modeByte & 0x01) != 0;
            var hasLinetype = (modeByte & 0x02) != 0;
            var hasHandling = (modeByte & 0x20) != 0;

            // Record layout after type(1)+mode(1)+size(2):
            //   layer_index s2, entity_common u16, then conditionals, then
            //   geometry. entity_size INCLUDES the type byte, so the record
            //   spans [cursor, cursor+size).
            var fieldCursor = cursor + 8; // past type+mode+size+layer+common

            // CIRCLE/ARC read an elevation f8 when has_elevation; LINE reads
            // z1/z2 INSTEAD when !has_elevation (no elevation field).
            if ((type == 3 || type == 8) && hasElevation)
            {
                fieldCursor += 8;
            }
            if (hasColor) fieldCursor += 1;
            if (hasLinetype) fieldCursor += 2;
            if (hasHandling)
            {
                if (fieldCursor >= data.Length)
                {
                    // Malformed handling pointer: skip the record.
                    cursor = recordEnd;
                    continue;
                }
                var len = data[fieldCursor];
                fieldCursor += 1 + len;
            }

            switch (type)
            {
                case 1: // LINE
                    if (TryParseLine(data, fieldCursor, hasElevation, out var line))
                        shapes.Add(line);
                    break;
                case 2: // POINT
                    if (TryParsePoint(data, fieldCursor, out var point))
                        shapes.Add(point);
                    break;
                case 3: // CIRCLE
                    if (TryParseCircle(data, fieldCursor, out var circle))
                        shapes.Add(circle);
                    break;
                case 8: // ARC
                    if (TryParseArc(data, fieldCursor, out var arc))
                        shapes.Add(arc);
                    break;
            }
            cursor = recordEnd;
        }

        return shapes;
    }

    // MARK: - Entity parsers

    /// <summary>LINE: x1 y1 [z1] x2 y2 [z2] as f8; z only when has_elevation == 0.</summary>
    private static bool TryParseLine(byte[] data, int offset, bool hasElevation, out VectorShape shape)
    {
        shape = default!;
        var o = offset;
        if (!TryReadF8(data, o, out var x1)) return false; o += 8;
        if (!TryReadF8(data, o, out var y1)) return false; o += 8;
        if (!hasElevation) o += 8; // z1
        if (!TryReadF8(data, o, out var x2)) return false; o += 8;
        if (!TryReadF8(data, o, out var y2)) return false;
        shape = VectorShape.Line(new VectorPoint(x1, y1), new VectorPoint(x2, y2));
        return true;
    }

    /// <summary>POINT: x y as f8 — rendered as a tiny circle (mirrors Swift).</summary>
    private static bool TryParsePoint(byte[] data, int offset, out VectorShape shape)
    {
        shape = default!;
        if (!TryReadF8(data, offset, out var x)) return false;
        if (!TryReadF8(data, offset + 8, out var y)) return false;
        shape = VectorShape.Circle(new VectorPoint(x, y), 0.05);
        return true;
    }

    /// <summary>CIRCLE: center x y (point_2d), radius f8.</summary>
    private static bool TryParseCircle(byte[] data, int offset, out VectorShape shape)
    {
        shape = default!;
        if (!TryReadF8(data, offset, out var cx)) return false;
        if (!TryReadF8(data, offset + 8, out var cy)) return false;
        if (!TryReadF8(data, offset + 16, out var radius)) return false;
        shape = VectorShape.Circle(new VectorPoint(cx, cy), radius);
        return true;
    }

    /// <summary>
    /// ARC: center x y, radius, angle_from, angle_to (radians, CCW from +X).
    /// VectorShape arcs are stored in degrees, so radians are converted.
    /// </summary>
    private static bool TryParseArc(byte[] data, int offset, out VectorShape shape)
    {
        shape = default!;
        if (!TryReadF8(data, offset, out var cx)) return false;
        if (!TryReadF8(data, offset + 8, out var cy)) return false;
        if (!TryReadF8(data, offset + 16, out var radius)) return false;
        if (!TryReadF8(data, offset + 24, out var angleFrom)) return false;
        if (!TryReadF8(data, offset + 32, out var angleTo)) return false;
        var arc = new VectorShape
        {
            Type = ShapeType.Arc,
            Radius = radius,
            StartAngleDeg = GeometryMath.RadToDeg(angleFrom),
            EndAngleDeg = GeometryMath.RadToDeg(angleTo)
        };
        arc.Points.Add(new VectorPoint(cx, cy));
        shape = arc;
        return true;
    }

    // MARK: - Readers

    private static bool TryReadF8(byte[] data, int offset, out double value)
    {
        value = 0;
        if (offset < 0 || offset + 8 > data.Length) return false;
        value = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(data.AsSpan(offset)));
        return true;
    }
}
