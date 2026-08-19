using System.Text.Json;
using System.Text.Json.Serialization;
using VectorPilot.Geometry;

namespace VectorPilot.Engine.IO;

// ---------------------------------------------------------------------------
// .shoppilot interop contract (mirrors the Swift Codable keys).
// Package layout:  <name>.shoppilot/
//                    manifest.json        { id, name, createdAt, updatedAt, version, sheetCount, documentVariables }
//                    toolpaths.json       [ PersistedToolpath ... ]  (pretty, sorted keys)
//                    sheets/<id>.json     Sheet (camelCase Codable keys: width/depth/height)
// ---------------------------------------------------------------------------

public sealed class ShopPilotManifest
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Untitled";
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
    public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("o");
    public string Version { get; set; } = "0.2";
    public int SheetCount { get; set; } = 1;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    /// <summary>
    /// Document variables. The Mac writes an ARRAY of {name,value} objects; declaring
    /// this as a Dictionary made every Mac document fail to load with
    /// "The JSON value could not be converted to Dictionary&lt;string,string&gt;".
    /// </summary>
    public List<DtoDocumentVariable>? DocumentVariables { get; set; }

    /// <summary>Keep-out zones (SPK-0308 persist). Null = document predates
    /// zones — legacy-safe decode.</summary>
    public List<KeepOutZoneDto>? KeepOutZones { get; set; }
}

/// <summary>Keep-out zone DTO (SPK-0308 persist; mirrors KeepOutZone).</summary>
public sealed class KeepOutZoneDto
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Keep Out";
    public string Type { get; set; } = "Rectangle";
    public double? CircleCenterX { get; set; }
    public double? CircleCenterY { get; set; }
    public double? CircleRadiusMm { get; set; }
    public double? RectMinX { get; set; }
    public double? RectMinY { get; set; }
    public double? RectMaxX { get; set; }
    public double? RectMaxY { get; set; }
    public List<List<double>>? PolygonPoints { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>Persisted toolpath (subset of the Mac PersistedToolpath; additive keys tolerated).</summary>
public sealed class PersistedToolpath
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Toolpath 1";
    public string Strategy { get; set; } = "Profile";
    public double CutDepth { get; set; }
    public double FeedRate { get; set; }
    public double SpindleSpeed { get; set; }
    public bool IsDirty { get; set; }
    public List<string> GCode { get; set; } = new();
}

/// <summary>Sheet DTO with the Mac's width/depth/height key naming.</summary>
/// <summary>
/// Material as the Mac writes it — a nested object, not a bare name string. Writing
/// a plain string (or null, which the ignore-null policy dropped entirely) means the
/// Mac cannot read the material back out of our documents.
/// </summary>
/// <summary>A document variable as the Mac writes it: {"name": …, "value": …}.</summary>
public sealed class DtoDocumentVariable
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}

public sealed class DtoMaterial
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Generic";
    public string Category { get; set; } = "Wood";
    public double Density { get; set; }
    public int HardnessRating { get; set; }
    public double MaxDepthOfCutMm { get; set; }
    public double MaxFeedRateMmPerMin { get; set; }
    public string CoolantType { get; set; } = "None";
}

public sealed class DtoSheet
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Sheet 1";
    public double Width { get; set; }
    public double Depth { get; set; }
    public double Height { get; set; }
    public string Units { get; set; } = "inches";
    public DtoMaterial? Material { get; set; }
    public bool IsDoubleSided { get; set; }
    public List<DtoLayer> Layers { get; set; } = new();
}
public sealed class DtoLayer
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Layer 1";

    // The Mac writes isVisible / isLocked / vectors. We were writing visible /
    // locked / shapes, so neither app could read the other's layers.
    [JsonPropertyName("isVisible")]
    public bool Visible { get; set; } = true;

    [JsonPropertyName("isLocked")]
    public bool Locked { get; set; }

    public string Color { get; set; } = "#2060C0";

    [JsonPropertyName("vectors")]
    public List<DtoShape> Shapes { get; set; } = new();

    /// <summary>Mac schema key; carried so a round-trip does not drop it.</summary>
    public List<string> ToolpathIds { get; set; } = new();
}

/// <summary>A point as the Mac writes it: {"x": …, "y": …}.</summary>
public sealed class DtoPoint
{
    public double X { get; set; }
    public double Y { get; set; }
}

public sealed class DtoShape
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Type { get; set; } = "polyline";

    [JsonPropertyName("points")]
    public List<DtoPoint> Points { get; set; } = new();

    public double Radius { get; set; }
    public double StartAngleDeg { get; set; }
    public double EndAngleDeg { get; set; }

    [JsonPropertyName("isClosed")]
    public bool Closed { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Text { get; set; }

    /// <summary>Mac schema keys.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Name { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? LayerId { get; set; }
}

public static class DocumentJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // ---- Engine Job -> DTO ----

    public static ShopPilotManifest ToManifest(Job job)
    {
        var manifest = new ShopPilotManifest
        {
            Id = job.Id.ToString(),
            Name = job.Name,
            CreatedAt = DateTime.UtcNow.ToString("o"),
            SheetCount = job.Sheets.Count,
            // Emit an empty array, not null: the Mac's schema always carries this key
            // and our ignore-null policy would drop it entirely.
            DocumentVariables = new List<DtoDocumentVariable>(),
            KeepOutZones = job.KeepOutZones.Count > 0 ? job.KeepOutZones.Select(ToZone).ToList() : null
        };
        return manifest;
    }

    // ---- Keep-out zones (SPK-0308 persist) ----

    public static KeepOutZoneDto ToZone(KeepOutZone zone) => new()
    {
        Id = zone.Id.ToString(),
        Name = zone.Name,
        Type = zone.Type.ToString(),
        CircleCenterX = zone.CircleCenter?.X,
        CircleCenterY = zone.CircleCenter?.Y,
        CircleRadiusMm = zone.CircleRadiusMm,
        RectMinX = zone.RectMinX,
        RectMinY = zone.RectMinY,
        RectMaxX = zone.RectMaxX,
        RectMaxY = zone.RectMaxY,
        PolygonPoints = zone.PolygonPoints?.Select(p => new List<double> { p.X, p.Y }).ToList(),
        IsActive = zone.IsActive
    };

    public static KeepOutZone FromZone(KeepOutZoneDto dto)
    {
        return new KeepOutZone
        {
            Id = Guid.TryParse(dto.Id, out var id) ? id : Guid.NewGuid(),
            Name = dto.Name,
            Type = Enum.TryParse<KeepOutZoneType>(dto.Type, ignoreCase: true, out var t) ? t : KeepOutZoneType.Rectangle,
            CircleCenter = dto.CircleCenterX is { } cx && dto.CircleCenterY is { } cy ? new VectorPoint(cx, cy) : null,
            CircleRadiusMm = dto.CircleRadiusMm,
            RectMinX = dto.RectMinX,
            RectMinY = dto.RectMinY,
            RectMaxX = dto.RectMaxX,
            RectMaxY = dto.RectMaxY,
            PolygonPoints = dto.PolygonPoints?.Select(p => p.Count >= 2 ? new VectorPoint(p[0], p[1]) : new VectorPoint(0, 0)).ToList(),
            IsActive = dto.IsActive
        };
    }

    public static DtoSheet ToSheet(Sheet sheet) => new()
    {
        Id = sheet.Id.ToString(),
        Name = sheet.Name,
        Width = sheet.Width,
        Depth = sheet.Height,
        Height = sheet.Thickness,
        Units = sheet.Units == UnitSystem.Inches ? "inches" : "mm",
        Material = ToMaterial(sheet.Material),
        Layers = sheet.Layers.Select(ToLayer).ToList()
    };

    /// <summary>Always emit a material object — the Mac's schema requires the key.</summary>
    public static DtoMaterial ToMaterial(Material? m) => new()
    {
        Name = m?.Name ?? "Generic",
        MaxDepthOfCutMm = m?.MaxDepthOfCutMm ?? 0,
        MaxFeedRateMmPerMin = m?.MaxFeedRateMmPerMin ?? 0
    };

    public static DtoLayer ToLayer(Layer layer) => new()
    {
        Id = layer.Id.ToString(),
        Name = layer.Name,
        Visible = layer.Visible,
        Locked = layer.Locked,
        Color = $"#{layer.Color.R:X2}{layer.Color.G:X2}{layer.Color.B:X2}",
        Shapes = layer.Shapes.Select(ToShape).ToList()
    };

    public static DtoShape ToShape(VectorShape shape) => new()
    {
        Id = shape.Id.ToString(),
        Type = ShapeTypeName(shape.Type),
        Points = shape.Points.Select(p => new DtoPoint { X = p.X, Y = p.Y }).ToList(),
        Radius = shape.Radius,
        StartAngleDeg = shape.StartAngleDeg,
        EndAngleDeg = shape.EndAngleDeg,
        Closed = shape.Closed,
        Text = string.IsNullOrEmpty(shape.Text) ? null : shape.Text
    };

    // ---- DTO -> Engine ----

    public static Job FromManifest(ShopPilotManifest manifest)
    {
        var job = Job.CreateEmpty();
        job.Name = manifest.Name;
        if (manifest.KeepOutZones is { } zones)
        {
            job.KeepOutZones.AddRange(zones.Select(FromZone));
        }
        return job;
    }

    public static Sheet FromSheet(DtoSheet dto)
    {
        var sheet = new Sheet
        {
            Name = dto.Name,
            Width = dto.Width,
            Height = dto.Depth,
            Thickness = dto.Height,
            Units = dto.Units.Equals("mm", StringComparison.OrdinalIgnoreCase) ? UnitSystem.Millimeters : UnitSystem.Inches
        };
        if (dto.Material is { } m && !string.IsNullOrEmpty(m.Name))
        {
            sheet.Material = new Material
            {
                Name = m.Name,
                MaxDepthOfCutMm = m.MaxDepthOfCutMm > 0 ? m.MaxDepthOfCutMm : 6.0,
                MaxFeedRateMmPerMin = m.MaxFeedRateMmPerMin > 0 ? m.MaxFeedRateMmPerMin : 1500
            };
        }
        sheet.Layers.Clear();
        foreach (var dtoLayer in dto.Layers) sheet.Layers.Add(FromLayer(dtoLayer));
        sheet.ActiveLayer = sheet.Layers.FirstOrDefault() ?? new Layer();
        return sheet;
    }

    public static Layer FromLayer(DtoLayer dto)
    {
        var layer = new Layer
        {
            Name = dto.Name,
            Visible = dto.Visible,
            Locked = dto.Locked
        };
        if (TryParseColor(dto.Color, out var color)) layer.Color = color;
        foreach (var dtoShape in dto.Shapes)
        {
            var shape = FromShape(dtoShape);
            if (shape is not null) layer.Shapes.Add(shape);
        }
        return layer;
    }

    public static VectorShape? FromShape(DtoShape dto)
    {
        var pts = dto.Points.Select(p => new VectorPoint(p.X, p.Y)).ToList();
        if (pts.Count == 0 && dto.Type != "circle") return null;
        var shape = dto.Type switch
        {
            "line" => pts.Count >= 2 ? VectorShape.Line(pts[0], pts[1]) : null,
            "rectangle" when pts.Count >= 2 => VectorShape.Rectangle(
                pts.Min(p => p.X), pts.Min(p => p.Y),
                pts.Max(p => p.X) - pts.Min(p => p.X), pts.Max(p => p.Y) - pts.Min(p => p.Y)),
            "circle" when pts.Count >= 1 => VectorShape.Circle(pts[0], dto.Radius),
            _ => VectorShape.Polyline(pts, dto.Closed)
        };
        if (shape is null) return null;
        shape.StartAngleDeg = dto.StartAngleDeg;
        shape.EndAngleDeg = dto.EndAngleDeg;
        shape.Text = dto.Text ?? "";
        return shape;
    }

    public static string ShapeTypeName(ShapeType t) => t switch
    {
        ShapeType.Line => "line",
        ShapeType.Rectangle => "rectangle",
        ShapeType.Circle => "circle",
        ShapeType.Arc => "arc",
        ShapeType.Ellipse => "ellipse",
        ShapeType.Text => "text",
        _ => "polyline"
    };

    private static bool TryParseColor(string hex, out System.Drawing.Color color)
    {
        color = System.Drawing.Color.Gray;
        try
        {
            var h = hex.TrimStart('#');
            if (h.Length == 6)
            {
                color = System.Drawing.Color.FromArgb(
                    Convert.ToInt32(h[..2], 16), Convert.ToInt32(h[2..4], 16), Convert.ToInt32(h[4..6], 16));
                return true;
            }
        }
        catch { /* ignore */ }
        return false;
    }
}
