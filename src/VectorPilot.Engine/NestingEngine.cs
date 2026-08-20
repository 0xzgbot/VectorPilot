using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>A single part placed on the stock sheet (ported from NestPart.swift).</summary>
public sealed class NestPart
{
    public required VectorShape Shape { get; init; }
    public VectorPoint Position { get; init; }
    /// <summary>Placement rotation in DEGREES (0 or 90). Was radians, which read as
    /// 1.57 degrees to every consumer and so never rotated anything.</summary>
    public double Rotation { get; init; }
    public int Index { get; init; }

    public double Area => ShapeArea(Shape);

    public BoundingBox BoundingBox
    {
        get
        {
            var local = Shape.Bounds();
            return new BoundingBox(
                local.MinX + Position.X, local.MinY + Position.Y,
                local.MaxX + Position.X, local.MaxY + Position.Y);
        }
    }

    public static double ShapeArea(VectorShape shape)
    {
        switch (shape.Type)
        {
            case ShapeType.Rectangle when shape.Points.Count >= 2:
            {
                var b = shape.Bounds();
                return b.Width * b.Height;
            }
            case ShapeType.Circle:
                return Math.PI * shape.Radius * shape.Radius;
            case ShapeType.Line:
                return 0;
            default:
            {
                var b = shape.Bounds();
                return b.Width * b.Height;
            }
        }
    }
}

/// <summary>Result of a nesting operation (ported from NestResult.swift).</summary>
public sealed class NestResult
{
    public List<NestPart> Parts { get; init; } = new();
    public double TotalPartArea { get; init; }
    public double SheetArea { get; init; }
    public double Utilization { get; init; }
    public int UnplacedCount { get; init; }
    public bool IsEmpty => Parts.Count == 0;
}

/// <summary>Rectangular shelf-packing nesting (ported from NestingEngine.swift).</summary>
public static class NestingEngine
{
    /// <param name="margin">Border kept clear at the sheet edge (mm).</param>
    /// <param name="spacing">
    /// Gap kept between neighbouring parts (mm). Previously parts were packed flush
    /// against each other — a 0mm kerf gap, which is uncuttable: the tool needs room for
    /// its own diameter between two outlines.
    /// </param>
    public static NestResult Nest(IReadOnlyList<VectorShape> parts, double sheetWidth, double sheetHeight, double margin = 5.0, double spacing = 0.0)
    {
        if (parts.Count == 0)
        {
            return new NestResult { SheetArea = sheetWidth * sheetHeight, Utilization = 0 };
        }

        double gap = Math.Max(0, spacing);
        double usableWidth = sheetWidth - 2 * margin;
        double usableHeight = sheetHeight - 2 * margin;

        var indexed = parts
            .Select((shape, i) => (Shape: shape, Bb: shape.Bounds(), Area: NestPart.ShapeArea(shape), Index: i))
            .OrderByDescending(p => p.Area)
            .ToList();

        var freeSpaces = new List<BoundingBox>
        {
            new(margin, margin, margin + usableWidth, margin + usableHeight)
        };

        var placed = new List<NestPart>();
        double totalPlacedArea = 0;
        int unplaced = 0;

        foreach (var item in indexed)
        {
            // Reserve the inter-part gap as part of the footprint, so the next part is
            // pushed clear of this one.
            double pw = item.Bb.Width + gap, ph = item.Bb.Height + gap;
            bool placedFlag = false;

            for (int si = 0; si < freeSpaces.Count; si++)
            {
                var space = freeSpaces[si];
                double sw = space.Width, sh = space.Height;

                if (pw <= sw && ph <= sh)
                {
                    var pos = new VectorPoint(space.MinX, space.MinY);
                    placed.Add(new NestPart { Shape = item.Shape, Position = pos, Rotation = 0, Index = item.Index });
                    totalPlacedArea += item.Area;
                    placedFlag = true;

                    double rightW = sw - pw;
                    if (rightW > 0)
                    {
                        freeSpaces.Add(new BoundingBox(space.MinX + pw, space.MinY, space.MaxX, space.MinY + ph));
                    }
                    double belowH = sh - ph;
                    if (belowH > 0)
                    {
                        freeSpaces.Add(new BoundingBox(space.MinX, space.MinY + ph, space.MaxX, space.MaxY));
                    }
                    freeSpaces.RemoveAt(si);
                    break;
                }

                if (ph <= sw && pw <= sh)
                {
                    var pos = new VectorPoint(space.MinX, space.MinY);
                    placed.Add(new NestPart { Shape = item.Shape, Position = pos, Rotation = 90, Index = item.Index });
                    totalPlacedArea += item.Area;
                    placedFlag = true;

                    double belowH = sh - pw;
                    if (belowH > 0)
                    {
                        freeSpaces.Add(new BoundingBox(space.MinX, space.MinY + pw, space.MaxX, space.MaxY));
                    }
                    double rightW = sw - ph;
                    if (rightW > 0)
                    {
                        freeSpaces.Add(new BoundingBox(space.MinX + ph, space.MinY, space.MaxX, space.MinY + pw));
                    }
                    freeSpaces.RemoveAt(si);
                    break;
                }
            }

            if (!placedFlag) unplaced++;
        }

        double sheet = sheetWidth * sheetHeight;
        return new NestResult
        {
            Parts = placed,
            TotalPartArea = totalPlacedArea,
            SheetArea = sheet,
            Utilization = sheet > 0 ? totalPlacedArea / sheet : 0,
            UnplacedCount = unplaced
        };
    }

    public static NestResult NestGrid(IReadOnlyList<VectorShape> parts, double sheetWidth, double sheetHeight, double spacing = 2.0)
    {
        if (parts.Count == 0)
        {
            return new NestResult { SheetArea = sheetWidth * sheetHeight, Utilization = 0 };
        }

        var bounds = parts
            .Select((shape, i) => (Bb: shape.Bounds(), Area: NestPart.ShapeArea(shape), Index: i))
            .OrderByDescending(p => p.Bb.Width)
            .ToList();

        var placed = new List<NestPart>();
        int unplaced = 0;
        double totalPlacedArea = 0;
        double cursorX = 0, cursorY = 0, rowHeight = 0;

        foreach (var item in bounds)
        {
            double w = item.Bb.Width, h = item.Bb.Height;
            if (cursorX + w > sheetWidth || cursorY + h > sheetHeight)
            {
                cursorX = 0;
                cursorY += rowHeight + spacing;
                rowHeight = h;
                if (cursorY + h > sheetHeight)
                {
                    unplaced++;
                    continue;
                }
            }

            placed.Add(new NestPart { Shape = parts[item.Index], Position = new VectorPoint(cursorX, cursorY), Rotation = 0, Index = item.Index });
            totalPlacedArea += item.Area;
            cursorX += w + spacing;
            rowHeight = Math.Max(rowHeight, h);
        }

        double sheet = sheetWidth * sheetHeight;
        return new NestResult
        {
            Parts = placed,
            TotalPartArea = totalPlacedArea,
            SheetArea = sheet,
            Utilization = sheet > 0 ? totalPlacedArea / sheet : 0,
            UnplacedCount = unplaced
        };
    }
}
