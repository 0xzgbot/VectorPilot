namespace VectorPilot.Engine;

/// <summary>
/// Index-paired diff between two toolpath g-code programs (SPK-0316 ghost diff).
/// Segments are the 2D moves produced by <see cref="WireframeRenderer.GenerateSegments"/>
/// (G0/G1 X/Y parsing via <see cref="WireframeRenderer.ParseXY"/>). Segment i in the
/// old program is paired with segment i in the new program: indices past the shorter
/// list are marked OnlyInOld / OnlyInNew respectively; indices present in both are
/// left unmarked (both flags false). Deterministic by construction — plain indexed
/// loops, no ordering-sensitive LINQ.
/// </summary>
public static class ToolpathDiff
{
    public static List<(double X0, double Y0, double X1, double Y1, bool OnlyInOld, bool OnlyInNew)> CompareLines(
        IReadOnlyList<string> oldGcode, IReadOnlyList<string> newGcode)
    {
        var oldSegs = WireframeRenderer.GenerateSegments(oldGcode);
        var newSegs = WireframeRenderer.GenerateSegments(newGcode);

        var result = new List<(double, double, double, double, bool, bool)>();
        int count = Math.Max(oldSegs.Count, newSegs.Count);
        for (int i = 0; i < count; i++)
        {
            if (i >= oldSegs.Count)
            {
                // Present only in the new program.
                var s = newSegs[i];
                result.Add((s.Start.X, s.Start.Y, s.End.X, s.End.Y, false, true));
            }
            else if (i >= newSegs.Count)
            {
                // Present only in the old program.
                var s = oldSegs[i];
                result.Add((s.Start.X, s.Start.Y, s.End.X, s.End.Y, true, false));
            }
            else
            {
                // Paired by index: present in both (coordinates taken from new).
                var s = newSegs[i];
                result.Add((s.Start.X, s.Start.Y, s.End.X, s.End.Y, false, false));
            }
        }
        return result;
    }
}
