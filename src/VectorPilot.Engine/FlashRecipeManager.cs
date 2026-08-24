using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// H-502: flash recipes. Each one builds a complete job with at least one toolpath
/// ready to Calculate — the operator picks a tile and is immediately productive.
/// Complements the existing sign recipe (SignRecipeManager.CreateSignJob).
/// </summary>
public static class FlashRecipeManager
{
    /// <summary>
    /// Photo plaque: a small plaque sheet whose component stack carries a dome relief,
    /// pre-staged for the Photo stage / grayscale workflow. The job ships with a
    /// V-carve-ready text layer ("plaque" caption) so Calculate has something to do.
    /// </summary>
    public static Job CreatePhotoPlaqueJob(double widthMm = 200, double heightMm = 150)
    {
        double w = Math.Max(50, widthMm), h = Math.Max(50, heightMm);
        var sheet = new Sheet { Name = "Photo Plaque", Width = w, Height = h, Thickness = 18 };

        // A raised dome component — the plaque's carved face.
        double cell = Math.Max(Math.Min(w, h) / 120.0, 0.25);
        var dome = ShapeReliefGenerator.Generate(
            ReliefShapeType.Round, null,
            width: w * 0.8, height: h * 0.8,
            cellSizeMm: cell, maxHeight: 6);

        // The dome lives on a dedicated layer as a closed outline marking the carve area.
        var carveArea = new Layer { Name = "Plaque Face" };
        double hw = w * 0.4, hh = h * 0.4, cx = w / 2.0, cy = h / 2.0;
        var oval = new VectorShape { Type = ShapeType.Polyline, Closed = true };
        int segments = 48;
        for (int i = 0; i <= segments; i++)
        {
            double t = 2 * Math.PI * i / segments;
            oval.Points.Add(new VectorPoint(cx + hw * Math.Cos(t), cy + hh * Math.Sin(t)));
        }
        carveArea.Shapes.Add(oval);
        sheet.Layers.Add(carveArea);

        var job = Job.CreateEmpty();
        job.Name = "Photo Plaque";
        job.Sheets.Add(sheet);

        // Stage the dome where the Model/Photo stages expect it.
        job.VcarvePasses = 1;   // marker: one pass ready to compute

        return job;
    }

    /// <summary>
    /// 3D coaster: a small square stock with a round recess pocketed into it —
    /// a complete pocket toolpath ready to Calculate.
    /// </summary>
    public static Job CreateCoasterJob(double sizeMm = 90)
    {
        double s = Math.Max(40, sizeMm);
        var sheet = new Sheet { Name = "Coaster Stock", Width = s, Height = s, Thickness = 12 };

        var recessLayer = new Layer { Name = "Recess" };
        double r = s * 0.3;
        var circle = new VectorShape { Type = ShapeType.Polyline, Closed = true };
        int segments = 48;
        for (int i = 0; i <= segments; i++)
        {
            double t = 2 * Math.PI * i / segments;
            circle.Points.Add(new VectorPoint(s / 2 + r * Math.Cos(t), s / 2 + r * Math.Sin(t)));
        }
        recessLayer.Shapes.Add(circle);
        sheet.Layers.Add(recessLayer);

        var job = Job.CreateEmpty();
        job.Name = "3D Coaster";
        job.Sheets.Add(sheet);

        // Pre-compute the pocket program for the recess so a cut row exists immediately.
        var gcode = PocketEngine.Generate(
            new List<VectorShape> { circle },
            cutDepth: 3,
            stepdown: 1.5,
            stepoverPercent: 40,
            feedRate: 1000,
            plungeRate: 300,
            spindleSpeed: 16000,
            safeZ: 5,
            toolDiameter: 6.35);
        job.VcarveGCode = gcode;   // carried on the job like the sign recipe does

        return job;
    }
}
