using VectorPilot.Geometry;

namespace VectorPilot.Engine;

/// <summary>
/// Weave relief generator. The estimator in <see cref="SweepExtrudeWeaveEngine"/>
/// returns volume/area numbers only; this produces the actual interlaced surface —
/// warp threads running in Y, weft in X, each raised where it passes OVER the other
/// according to the pattern, so the result can be machined as a 3D relief.
/// </summary>
public static class WeaveReliefGenerator
{
    /// <summary>
    /// Build an interlaced weave heightfield covering width × height (mm).
    /// Thread crowns reach <paramref name="threadHeight"/>; the surface dips to the
    /// under-thread's crown where it passes beneath.
    /// </summary>
    public static HeightfieldData Generate(
        WeaveParams p,
        double width,
        double height,
        double cellSizeMm = 0.5,
        double threadHeight = 2.0)
    {
        int nx = Math.Max(2, (int)Math.Round(width / cellSizeMm));
        int ny = Math.Max(2, (int)Math.Round(height / cellSizeMm));
        var heights = new double[nx * ny];

        // Thread pitch: one warp per column band, one weft per row band.
        double warpPitch = width / Math.Max(1, p.WarpCount);
        double weftPitch = height / Math.Max(1, p.WeftCount);
        double halfThread = Math.Max(p.ThreadSize, cellSizeMm) / 2.0;

        for (int j = 0; j < ny; j++)
        {
            double y = (j + 0.5) * cellSizeMm;
            int weftIndex = (int)(y / weftPitch);
            double weftCentre = (weftIndex + 0.5) * weftPitch;
            double weftOffset = Math.Abs(y - weftCentre);

            for (int i = 0; i < nx; i++)
            {
                double x = (i + 0.5) * cellSizeMm;
                int warpIndex = (int)(x / warpPitch);
                double warpCentre = (warpIndex + 0.5) * warpPitch;
                double warpOffset = Math.Abs(x - warpCentre);

                // Rounded cross-section for each thread: 0 at the edge, 1 at the crown.
                double warpProfile = Profile(warpOffset, halfThread);
                double weftProfile = Profile(weftOffset, halfThread);

                bool warpOver = WarpIsOver(p.Pattern, warpIndex, weftIndex);

                double h;
                if (warpProfile <= 0 && weftProfile <= 0)
                {
                    h = 0;                                   // gap between threads
                }
                else if (warpOver)
                {
                    // Warp on top: its crown wins; weft shows only where warp is absent.
                    h = warpProfile > 0 ? threadHeight * warpProfile
                                        : threadHeight * weftProfile * p.Overlap;
                }
                else
                {
                    h = weftProfile > 0 ? threadHeight * weftProfile
                                        : threadHeight * warpProfile * p.Overlap;
                }

                heights[j * nx + i] = h;
            }
        }

        return new HeightfieldData(nx, ny, cellSizeMm, 0, 0, heights);
    }

    /// <summary>Rounded thread cross-section: 1 at the centreline, 0 at the edge.</summary>
    private static double Profile(double offset, double halfThread)
    {
        if (offset >= halfThread) return 0;
        double t = offset / halfThread;                 // 0..1
        return Math.Sqrt(Math.Max(0, 1 - t * t));      // circular arc
    }

    /// <summary>
    /// Which thread passes over at this crossing.
    /// Plain = 1/1 alternating, Twill = 2/2 diagonal, Satin = 4/1 scattered float.
    /// </summary>
    public static bool WarpIsOver(WeavePattern pattern, int warp, int weft) => pattern switch
    {
        WeavePattern.Plain => (warp + weft) % 2 == 0,
        WeavePattern.Twill => ((warp + weft) % 4) < 2,
        WeavePattern.Satin => (warp * 2 + weft) % 5 == 0,
        _ => (warp + weft) % 2 == 0
    };
}
