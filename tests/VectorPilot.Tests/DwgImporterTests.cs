using System.Buffers.Binary;
using System.IO;
using System.Text;
using VectorPilot.Engine;
using VectorPilot.Geometry;
using Xunit;

namespace VectorPilot.Tests;

/// <summary>
/// Tests for the R12 (AC1009) binary DWG importer. The fixture is a minimal
/// REAL R12 DWG byte layout: 12-byte "AC1009\0\0\0\0\0\0" magic, header
/// padding, u32-le entities_start (0x14) / entities_end (0x18), then entity
/// records of type(1)+mode(1)+size(2)+layer(2)+common(2)+geometry, where
/// entity_size includes the type byte.
/// </summary>
public class DwgImporterTests
{
    [Fact]
    public void Parse_R12Fixture_ExtractsLinePointCircleArc()
    {
        var shapes = DwgImporter.Parse(BuildR12Fixture());

        Assert.Equal(5, shapes.Count);

        // LINE (0,0) -> (10,5), mode 0 (no z, no elevation).
        var line = shapes[0];
        Assert.Equal(ShapeType.Line, line.Type);
        Assert.Equal(new VectorPoint(0, 0), line.Points[0]);
        Assert.Equal(new VectorPoint(10, 5), line.Points[1]);

        // POINT (3,4) -> tiny circle, radius 0.05 (mirrors Swift).
        var point = shapes[1];
        Assert.Equal(ShapeType.Circle, point.Type);
        Assert.Equal(new VectorPoint(3, 4), point.Points[0]);
        Assert.Equal(0.05, point.Radius, 9);

        // CIRCLE center (1,2) radius 5.
        var circle = shapes[2];
        Assert.Equal(ShapeType.Circle, circle.Type);
        Assert.Equal(new VectorPoint(1, 2), circle.Points[0]);
        Assert.Equal(5, circle.Radius, 9);

        // ARC center (0,0) radius 2, 0..pi/2 radians -> 0..90 degrees.
        var arc = shapes[3];
        Assert.Equal(ShapeType.Arc, arc.Type);
        Assert.Equal(new VectorPoint(0, 0), arc.Points[0]);
        Assert.Equal(2, arc.Radius, 9);
        Assert.Equal(0, arc.StartAngleDeg, 9);
        Assert.Equal(90, arc.EndAngleDeg, 9);

        // CIRCLE with has_elevation (mode 0x04): elevation f8 precedes geometry.
        var elevated = shapes[4];
        Assert.Equal(ShapeType.Circle, elevated.Type);
        Assert.Equal(new VectorPoint(-1, 0.5), elevated.Points[0]);
        Assert.Equal(3.5, elevated.Radius, 9);
    }

    [Fact]
    public void Parse_NonAc1009Version_ReturnsEmpty()
    {
        // Post-R12 (bit-coded) DWG header, e.g. AC1015.
        var data = Encoding.ASCII.GetBytes("AC1015").Concat(new byte[40]).ToArray();
        Assert.Empty(DwgImporter.Parse(data));
    }

    [Fact]
    public void Parse_TooSmall_ReturnsEmpty()
    {
        Assert.Empty(DwgImporter.Parse(new byte[0x10]));
    }

    [Fact]
    public void Parse_Null_ReturnsEmpty()
    {
        Assert.Empty(DwgImporter.Parse(null!));
    }

    [Fact]
    public void Parse_CorruptSectionBounds_ReturnsEmpty()
    {
        // Valid magic but entities_end points past the file.
        var data = BuildR12Fixture();
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x18), (uint)(data.Length + 100));
        Assert.Empty(DwgImporter.Parse(data));
    }

    [Fact]
    public void Parse_TrailingSentinelNoise_StopsCleanly()
    {
        // A record whose size would run past entities_end stops the scan
        // instead of throwing; the valid LINE record before it still parses.
        var data = BuildR12Fixture();
        // Append a bogus record header claiming a huge size at the end.
        var bogus = new byte[8];
        bogus[0] = 1; // LINE
        bogus[1] = 0; // mode
        BinaryPrimitives.WriteUInt16LittleEndian(bogus.AsSpan(2), 0xFFFF);
        var extended = data.Concat(bogus).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(extended.AsSpan(0x18), (uint)extended.Length);

        var shapes = DwgImporter.Parse(extended);
        Assert.Equal(5, shapes.Count);
    }

    [Fact]
    public void Parse_UnsupportedEntityType_Skipped()
    {
        // Type 4 (POLYLINE_3D) is not in the LINE/POINT/CIRCLE/ARC slice.
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        WriteRecordHeader(bw, 4, 0x00, payload: p => p.Write(1.0)); // 4-byte payload
        var bytes = ms.ToArray();
        var start = 0x1C;
        var data = BuildHeader(start, start + bytes.Length).Concat(bytes).ToArray();

        Assert.Empty(DwgImporter.Parse(data));
    }

    [Fact]
    public void Parse_ExoticModeFlags_SkipWholeRecord()
    {
        // mode 0x80 (has_attributes) -> record skipped by entity_size.
        var ms = new MemoryStream();
        var bw = new BinaryWriter(ms);
        WriteRecordHeader(bw, 1, 0x80, p => { p.Write(1.0); p.Write(2.0); p.Write(3.0); p.Write(4.0); });
        // A valid LINE after the skipped record still parses (x1 y1 z1 x2 y2).
        WriteRecordHeader(bw, 1, 0x00, p => { p.Write(0.0); p.Write(0.0); p.Write(0.0); p.Write(7.0); p.Write(8.0); });
        var bytes = ms.ToArray();
        var start = 0x1C;
        var data = BuildHeader(start, start + bytes.Length).Concat(bytes).ToArray();

        var shapes = DwgImporter.Parse(data);
        var line = Assert.Single(shapes);
        Assert.Equal(ShapeType.Line, line.Type);
        Assert.Equal(new VectorPoint(7, 8), line.Points[1]);
    }

    // MARK: - R12 fixture builders

    /// <summary>Minimal AC1009 file: LINE + POINT + CIRCLE + ARC + elevated CIRCLE.</summary>
    private static byte[] BuildR12Fixture()
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms, Encoding.UTF8, leaveOpen: true);

        bw.Write(Encoding.ASCII.GetBytes("AC1009"));
        bw.Write(new byte[6]); // null padding -> 12-byte magic
        bw.Write(new byte[8]); // 0x0C..0x13 padding
        bw.Write(0u);          // 0x14 entities_start (patched below)
        bw.Write(0u);          // 0x18 entities_end (patched below)

        // LINE (0,0,0) -> (10,5), mode 0: x1 y1 z1 x2 y2 (z1 present, no elevation).
        WriteRecordHeader(bw, 1, 0x00, p => { p.Write(0.0); p.Write(0.0); p.Write(0.0); p.Write(10.0); p.Write(5.0); });
        // POINT (3,4), mode 0.
        WriteRecordHeader(bw, 2, 0x00, p => { p.Write(3.0); p.Write(4.0); });
        // CIRCLE center (1,2) r=5, mode 0.
        WriteRecordHeader(bw, 3, 0x00, p => { p.Write(1.0); p.Write(2.0); p.Write(5.0); });
        // ARC center (0,0) r=2, 0..pi/2 (radians), mode 0.
        WriteRecordHeader(bw, 8, 0x00, p => { p.Write(0.0); p.Write(0.0); p.Write(2.0); p.Write(0.0); p.Write(Math.PI / 2); });
        // CIRCLE center (-1,0.5) r=3.5 with has_elevation (mode 0x04):
        // elevation f8 comes before the geometry.
        WriteRecordHeader(bw, 3, 0x04, p => { p.Write(9.0); p.Write(-1.0); p.Write(0.5); p.Write(3.5); });

        var bytes = ms.ToArray();
        var start = 0x1C;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x14), (uint)start);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x18), (uint)bytes.Length);
        return bytes;
    }

    /// <summary>Header-only AC1009 bytes with the given absolute entity bounds.</summary>
    private static byte[] BuildHeader(int start, int end)
    {
        var data = new byte[0x1C];
        Encoding.ASCII.GetBytes("AC1009").CopyTo(data, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x14), (uint)start);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x18), (uint)end);
        return data;
    }

    /// <summary>
    /// Writes type(1)+mode(1)+size(2)+layer(2)+common(2) then the payload, with
    /// size = TOTAL record length including the 8-byte header (Swift semantics).
    /// </summary>
    private static void WriteRecordHeader(BinaryWriter bw, byte type, byte mode, Action<BinaryWriter> payload)
    {
        var recordStart = bw.BaseStream.Position;
        bw.Write(type);
        bw.Write(mode);
        bw.Write((ushort)0); // size placeholder
        bw.Write((ushort)0); // layer_index
        bw.Write((ushort)0); // entity_common
        payload(bw);
        var end = bw.BaseStream.Position;
        var size = (ushort)(end - recordStart);
        bw.BaseStream.Position = recordStart + 2;
        bw.Write(size);
        bw.BaseStream.Position = end;
    }
}
