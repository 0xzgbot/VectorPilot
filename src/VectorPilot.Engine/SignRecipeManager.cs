using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Sign recipe manager (ported from SignRecipeManager.swift, SPK-0510/1106):
/// creates a sign job — text-on-curve → decorative border → V-Carve toolpath.
/// </summary>
public static class SignRecipeManager
{
    /// <summary>Create a complete sign job from the signage recipe.</summary>
    public static Job CreateSignJob(
        string jobName = "Sign Job",
        string text = "SHOP",
        List<TextTool.GlyphOutline>? glyphs = null,
        double vBitAngle = 90.0,
        double vCarveDepth = 0.5,
        double feedRate = 1000.0)
    {
        double stockW = 457.2, stockD = 609.6, stockH = 19.05;
        var sheet = new Sheet { Name = "Sign Sheet", Width = stockW, Height = stockD, Thickness = stockH };

        // Layer 1: Text on curve
        var textLayer = new Layer { Name = "Text" };
        var center = new VectorPoint(stockW / 2, stockD / 2 + 50);
        var arcPts = ArcPoints(center, 120, -0.8, 0.8, 50);

        // Generate placeholder glyphs if not provided (engine-testable path)
        if (glyphs is null)
        {
            glyphs = new List<TextTool.GlyphOutline>();
            foreach (char c in text)
            {
                glyphs.Add(PlaceholderGlyph(c));
            }
        }

        var textShapes = TextTool.TextOnCurve(glyphs, arcPts, 1.0, 0.5, 0.0);
        for (int i = 0; i < textShapes.Count; i++)
        {
            if (i < text.Length) textShapes[i].Text = text[i].ToString();
            textLayer.Shapes.Add(textShapes[i]);
        }
        sheet.Layers.Add(textLayer);

        // Layer 2: Decorative border
        var borderLayer = new Layer { Name = "Border" };
        var border = CreateDecorativeBorder(stockW - 40, stockD - 40);
        var borderInStock = new VectorShape { Type = ShapeType.Polyline, Closed = true };
        borderInStock.Points.AddRange(border.Points.Select(p => new VectorPoint(p.X + stockW / 2, p.Y + stockD / 2)));
        borderLayer.Shapes.Add(borderInStock);
        sheet.Layers.Add(borderLayer);

        var job = Job.CreateEmpty();
        job.Name = jobName;
        job.Sheets.Add(sheet);

        // Pre-calculate V-Carve for text vectors
        var textVectors = textLayer.Shapes;
        if (textVectors.Count > 0)
        {
            var vectorDepths = new Dictionary<Guid, double>();
            foreach (var vec in textVectors) vectorDepths[vec.Id] = vCarveDepth;

            var vcParams = new VCarveParams
            {
                VBitAngleDegrees = vBitAngle,
                FeedRateMmPerMin = feedRate,
                PlungeFeedRateMmPerMin = 300,
                MaxDepthOfCutMm = vCarveDepth,
                LeadInDistanceMm = 5,
                LeadOutDistanceMm = 5,
                StepOverMm = 1.0,
                FlatBottomMode = false,
                VectorDepths = vectorDepths
            };

            var vcResult = VCarveEngine.Compute(textVectors, vcParams, stockH);
            job.VcarvePasses = vcResult.PassCount;
            job.VcarveTimeSeconds = vcResult.EstimatedTimeSeconds;
            job.VcarveGCode = vcResult.GcodeLines;
            job.VcarveParamsJSON = System.Text.Json.JsonSerializer.Serialize(vcParams);
        }

        return job;
    }

    /// <summary>Placeholder glyph (engine-testable without WPF).</summary>
    private static TextTool.GlyphOutline PlaceholderGlyph(char c)
    {
        // Simple box outline as placeholder glyph (2x4 units)
        var pts = new List<VectorPoint>
        {
            new(0, 0), new(2, 0), new(2, 4), new(0, 4), new(0, 0)
        };
        return new TextTool.GlyphOutline { Points = pts, Advance = 2.5 };
    }

    private static VectorShape CreateDecorativeBorder(double width, double height, double cornerRadius = 10.0)
    {
        var halfW = width / 2.0;
        var halfH = height / 2.0;
        int segments = 16;
        var pts = new List<VectorPoint>();

        for (int i = 0; i <= segments; i++)
        {
            double t = (double)i / segments;
            pts.Add(new VectorPoint(-halfW + cornerRadius + (width - 2 * cornerRadius) * t, halfH - cornerRadius));
        }
        for (int i = 1; i <= segments; i++)
        {
            double angle = -Math.PI / 2.0 * i / segments;
            pts.Add(new VectorPoint(cornerRadius * Math.Cos(angle), cornerRadius * Math.Sin(angle) + halfH - cornerRadius));
        }
        for (int i = 0; i <= segments; i++)
        {
            double t = (double)i / segments;
            pts.Add(new VectorPoint(halfW - cornerRadius, halfH - cornerRadius - (height - 2 * cornerRadius) * t));
        }
        for (int i = 1; i <= segments; i++)
        {
            double angle = Math.PI / 2.0 + Math.PI / 2.0 * i / segments;
            pts.Add(new VectorPoint(cornerRadius * Math.Cos(angle), cornerRadius * Math.Sin(angle) - halfH + cornerRadius));
        }
        for (int i = 0; i <= segments; i++)
        {
            double t = (double)i / segments;
            pts.Add(new VectorPoint(halfW - cornerRadius - (width - 2 * cornerRadius) * t, -halfH + cornerRadius));
        }
        for (int i = 1; i <= segments; i++)
        {
            double angle = Math.PI + Math.PI / 2.0 * i / segments;
            pts.Add(new VectorPoint(cornerRadius * Math.Cos(angle), cornerRadius * Math.Sin(angle) - halfH + cornerRadius));
        }
        for (int i = 0; i <= segments; i++)
        {
            double t = (double)i / segments;
            pts.Add(new VectorPoint(-halfW + cornerRadius, -halfH + cornerRadius + (height - 2 * cornerRadius) * t));
        }
        for (int i = 1; i <= segments; i++)
        {
            double angle = 3.0 * Math.PI / 2.0 + Math.PI / 2.0 * i / segments;
            pts.Add(new VectorPoint(cornerRadius * Math.Cos(angle), cornerRadius * Math.Sin(angle) + halfH - cornerRadius));
        }
        pts.Add(pts[0]);

        var shape = new VectorShape { Type = ShapeType.Polyline, Closed = true };
        shape.Points.AddRange(pts);
        return shape;
    }

    /// <summary>Generate points along a circular arc.</summary>
    public static List<VectorPoint> ArcPoints(VectorPoint center, double radius, double startAngle, double endAngle, int segments)
    {
        var pts = new List<VectorPoint>();
        for (int i = 0; i <= segments; i++)
        {
            double t = (double)i / segments;
            double angle = startAngle + (endAngle - startAngle) * t;
            pts.Add(new VectorPoint(center.X + radius * Math.Cos(angle), center.Y + radius * Math.Sin(angle)));
        }
        return pts;
    }
}
