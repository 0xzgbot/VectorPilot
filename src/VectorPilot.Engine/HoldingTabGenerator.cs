using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>Where a tab sits along a contour and how large it is.</summary>
public sealed class HoldingTab
{
    /// <summary>Distance along the contour, in mm from the start point.</summary>
    public double Position { get; init; }
    /// <summary>Tab length along the path (mm).</summary>
    public double Length { get; init; } = 6.0;
    /// <summary>Height of material left under the cutter (mm).</summary>
    public double Height { get; init; } = 1.0;
}

/// <summary>
/// Holding tabs / bridges for profile cuts. A part cut fully free on the last
/// pass can lift and be thrown by the cutter; tabs leave short spans of material
/// so the part stays anchored until it is cut or snapped out by hand.
/// </summary>
public static class HoldingTabGenerator
{
    /// <summary>Total length of a contour, closing the loop when required.</summary>
    public static double ContourLength(IReadOnlyList<VectorPoint> pts, bool closed)
    {
        if (pts.Count < 2) return 0;

        double total = 0;
        for (int i = 0; i + 1 < pts.Count; i++) total += pts[i].DistanceTo(pts[i + 1]);
        if (closed && pts.Count > 2) total += pts[^1].DistanceTo(pts[0]);
        return total;
    }

    /// <summary>
    /// Space <paramref name="count"/> tabs evenly around a contour. Returns an
    /// empty list when the contour is too short to hold them without overlapping.
    /// </summary>
    public static List<HoldingTab> Distribute(
        IReadOnlyList<VectorPoint> pts,
        bool closed,
        int count,
        double tabLength = 6.0,
        double tabHeight = 1.0)
    {
        var tabs = new List<HoldingTab>();
        if (count <= 0 || tabLength <= 0) return tabs;

        double length = ContourLength(pts, closed);
        if (length <= 0) return tabs;

        // Refuse to place tabs that would touch or overlap each other.
        if (count * tabLength >= length) return tabs;

        double spacing = length / count;
        for (int i = 0; i < count; i++)
        {
            // Centre each tab in its span so the first tab does not sit on the
            // start point, where lead-in moves usually land.
            double centre = spacing * i + spacing / 2.0;
            tabs.Add(new HoldingTab
            {
                Position = centre - tabLength / 2.0,
                Length = tabLength,
                Height = tabHeight
            });
        }
        return tabs;
    }

    /// <summary>
    /// True when the given distance along the contour falls inside a tab, and so
    /// should be cut at the tab height instead of full depth.
    /// </summary>
    public static bool IsInTab(double distanceAlong, IReadOnlyList<HoldingTab> tabs)
        => TabAt(distanceAlong, tabs) is not null;

    /// <summary>The tab covering this distance, if any.</summary>
    public static HoldingTab? TabAt(double distanceAlong, IReadOnlyList<HoldingTab> tabs)
    {
        foreach (var t in tabs)
        {
            if (distanceAlong >= t.Position && distanceAlong <= t.Position + t.Length)
                return t;
        }
        return null;
    }

    /// <summary>
    /// Z for a point at <paramref name="distanceAlong"/>: the requested cut depth
    /// normally, or the tab's top surface while crossing a tab. Tabs never make a
    /// cut deeper than requested.
    /// </summary>
    public static double DepthAt(double distanceAlong, double cutZ, IReadOnlyList<HoldingTab> tabs)
    {
        if (TabAt(distanceAlong, tabs) is not { } tab) return cutZ;

        // cutZ is negative (below the surface). A tab of height h leaves material
        // from the bottom of the stock, so the cutter rises to cutZ + h.
        double tabZ = cutZ + tab.Height;
        return Math.Min(tabZ, 0);   // never above the stock surface
    }
}
