namespace VectorPilot.Engine;

/// <summary>Fade ramp direction (ported from FadeDirection).</summary>
public enum FadeDirection
{
    None, LeftToRight, RightToLeft, TopToBottom, BottomToTop, CenterOut, Radial
}

/// <summary>
/// Per-component dynamic height/tilt/fade modifiers (ported from
/// ComponentModifierEngine.swift, SPK-0702). All ops are grid-preserving:
/// width/height/cellSizeMm/minX/minY never change — only heights do.
/// </summary>
public static class ComponentModifierEngine
{
    /// <summary>Multiply every height by scale, clamped ≥ 0.</summary>
    public static HeightfieldData HeightScaled(HeightfieldData hf, double scale)
    {
        if (Math.Abs(scale - 1.0) <= 1e-9) return hf;
        return New(hf, hf.Heights.Select(h => Math.Max(0, h * scale)).ToArray());
    }

    /// <summary>Rotate the relief about its grid center by angleDegrees (CCW, +y up),
    /// re-sampling bilinearly onto the SAME grid. Rotated-in emptiness reads 0.</summary>
    public static HeightfieldData Tilted(HeightfieldData hf, double angleDegrees)
    {
        if (Math.Abs(angleDegrees % 360.0) <= 1e-6) return hf;
        double theta = angleDegrees * Math.PI / 180.0;
        double cosT = Math.Cos(theta), sinT = Math.Sin(theta);
        double cx = hf.MinX + hf.Width * hf.CellSizeMm / 2.0;
        double cy = hf.MinY + hf.Height * hf.CellSizeMm / 2.0;
        var out_ = new double[hf.Heights.Length];
        for (int j = 0; j < hf.Height; j++)
        {
            for (int i = 0; i < hf.Width; i++)
            {
                double wx = hf.MinX + (i + 0.5) * hf.CellSizeMm;
                double wy = hf.MinY + (j + 0.5) * hf.CellSizeMm;
                double dx = wx - cx, dy = wy - cy;
                double sx = cx + dx * cosT + dy * sinT;
                double sy = cy - dx * sinT + dy * cosT;
                out_[j * hf.Width + i] = Sample(hf, sx, sy);
            }
        }
        return New(hf, out_);
    }

    /// <summary>Multiply heights by a ramp from 1.0 at the "full" edge down to
    /// 1 − fadeAmount at the opposite edge. amount clamped [0,1]; 0 = no-op.</summary>
    public static HeightfieldData Faded(HeightfieldData hf, double amount, FadeDirection direction)
    {
        double amt = Math.Clamp(amount, 0.0, 1.0);
        if (amt <= 1e-9 || direction == FadeDirection.None) return hf;
        double w = hf.Width, h = hf.Height;
        double cx = (w - 1) / 2.0, cy = (h - 1) / 2.0;
        var out_ = (double[])hf.Heights.Clone();
        for (int j = 0; j < hf.Height; j++)
        {
            for (int i = 0; i < hf.Width; i++)
            {
                double factor = direction switch
                {
                    FadeDirection.LeftToRight => 1.0 - amt * (w > 1 ? i / (w - 1) : 0),
                    FadeDirection.RightToLeft => 1.0 - amt * (w > 1 ? (w - 1 - i) / (w - 1) : 0),
                    FadeDirection.TopToBottom => 1.0 - amt * (h > 1 ? j / (h - 1) : 0),
                    FadeDirection.BottomToTop => 1.0 - amt * (h > 1 ? (h - 1 - j) / (h - 1) : 0),
                    FadeDirection.CenterOut => 1.0 - amt * Math.Min(1.0, Math.Max(
                        w > 1 ? Math.Abs(i - cx) / Math.Max(cx, 1) : 0,
                        h > 1 ? Math.Abs(j - cy) / Math.Max(cy, 1) : 0)),
                    FadeDirection.Radial => 1.0 - amt * Math.Min(1.0, Math.Sqrt(
                        Math.Pow(w > 1 ? (i - cx) / Math.Max(cx, 1) : 0, 2) +
                        Math.Pow(h > 1 ? (j - cy) / Math.Max(cy, 1) : 0, 2))),
                    _ => 1.0
                };
                out_[j * hf.Width + i] = Math.Max(0, hf.Heights[j * hf.Width + i] * factor);
            }
        }
        return New(hf, out_);
    }

    /// <summary>Apply scale → tilt → fade in order; null props are skipped.</summary>
    public static HeightfieldData Apply(HeightfieldData hf, double? heightScale, double? tiltAngleDegrees, double? fadeAmount, FadeDirection? fadeDirection)
    {
        var out_ = hf;
        if (heightScale is { } scale) out_ = HeightScaled(out_, scale);
        if (tiltAngleDegrees is { } tilt) out_ = Tilted(out_, tilt);
        if (fadeAmount is { } amount && fadeDirection is { } dir) out_ = Faded(out_, amount, dir);
        return out_;
    }

    private static HeightfieldData New(HeightfieldData hf, double[] heights)
        => new(hf.Width, hf.Height, hf.CellSizeMm, hf.MinX, hf.MinY, heights);

    /// <summary>Bilinear sample; outside the footprint reads 0.</summary>
    private static double Sample(HeightfieldData hf, double worldX, double worldY)
    {
        double fx = (worldX - hf.MinX) / hf.CellSizeMm - 0.5;
        double fy = (worldY - hf.MinY) / hf.CellSizeMm - 0.5;
        if (fx < -1e-9 || fy < -1e-9 || fx > hf.Width - 1 + 1e-9 || fy > hf.Height - 1 + 1e-9) return 0;
        int x0 = (int)fx, y0 = (int)fy;
        int x1 = Math.Min(x0 + 1, hf.Width - 1);
        int y1 = Math.Min(y0 + 1, hf.Height - 1);
        double tx = fx - x0, ty = fy - y0;
        double h00 = hf.Heights[y0 * hf.Width + x0];
        double h10 = hf.Heights[y0 * hf.Width + x1];
        double h01 = hf.Heights[y1 * hf.Width + x0];
        double h11 = hf.Heights[y1 * hf.Width + x1];
        double top = h00 + (h10 - h00) * tx;
        double bottom = h01 + (h11 - h01) * tx;
        return Math.Max(0, top + (bottom - top) * ty);
    }
}
