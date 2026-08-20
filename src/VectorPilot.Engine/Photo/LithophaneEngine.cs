using System;
using System.Collections.Generic;

namespace VectorPilot.Engine;

/// <summary>
/// Parameters controlling lithophane thickness mapping.
/// </summary>
public sealed class LithophaneParams
{
    /// <summary>Thinnest material emitted, in millimetres. Maps to white (fully lit) pixels.</summary>
    public double MinThicknessMm { get; set; } = 0.8;

    /// <summary>Thickest material emitted, in millimetres. Maps to black (fully blocked) pixels.</summary>
    public double MaxThicknessMm { get; set; } = 3.5;

    /// <summary>Grid pitch of the emitted heightfield, in millimetres.</summary>
    public double CellSizeMm { get; set; } = 0.2;

    /// <summary>When true, swaps the mapping so light pixels become thicker.</summary>
    public bool Invert { get; set; }
}

/// <summary>
/// Turns greyscale luminance into a thickness heightfield for a backlit lithophane.
/// </summary>
/// <remarks>
/// DIRECTION: DARK pixels produce THICKER material, because thicker material blocks more
/// transmitted light and therefore reads as dark when the panel is lit from behind.
/// LIGHT pixels produce THINNER material, letting more light through.
/// Setting <see cref="LithophaneParams.Invert"/> swaps this relationship.
/// </remarks>
public static class LithophaneEngine
{
    /// <summary>
    /// Computes the lithophane thickness field for the supplied luminance image.
    /// </summary>
    /// <param name="luminance">Row-major luminance samples in 0..1 (0 = black, 1 = white).</param>
    /// <param name="width">Image width in cells.</param>
    /// <param name="height">Image height in cells.</param>
    /// <param name="p">Thickness mapping parameters.</param>
    /// <returns>
    /// A heightfield whose heights are thicknesses in millimetres, clamped into
    /// [MinThicknessMm, MaxThicknessMm]; null when the inputs are inconsistent.
    /// </returns>
    public static HeightfieldData? Compute(
        IReadOnlyList<double> luminance,
        int width,
        int height,
        LithophaneParams p)
    {
        if (luminance is null || p is null)
        {
            return null;
        }

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        long expected = (long)width * height;
        if (luminance.Count != expected)
        {
            return null;
        }

        double min = p.MinThicknessMm;
        double max = p.MaxThicknessMm;
        double cell = p.CellSizeMm;

        if (double.IsNaN(min) || double.IsInfinity(min))
        {
            min = 0.0;
        }

        if (double.IsNaN(cell) || double.IsInfinity(cell) || cell <= 0.0)
        {
            cell = 0.2;
        }

        var heights = new double[width * height];

        if (double.IsNaN(max) || double.IsInfinity(max) || max <= min)
        {
            // Degenerate range: emit a uniform field at the minimum thickness.
            for (int i = 0; i < heights.Length; i++)
            {
                heights[i] = min;
            }

            return new HeightfieldData(width, height, cell, 0, 0, heights);
        }

        double span = max - min;

        for (int i = 0; i < heights.Length; i++)
        {
            double lum = luminance[i];

            if (double.IsNaN(lum) || double.IsInfinity(lum) || lum < 0.0 || lum > 1.0)
            {
                lum = 0.5;
            }

            // Dark (lum = 0) -> thickest; light (lum = 1) -> thinnest.
            double t = p.Invert ? lum : 1.0 - lum;
            double thickness = min + (t * span);

            if (thickness < min)
            {
                thickness = min;
            }
            else if (thickness > max)
            {
                thickness = max;
            }

            heights[i] = thickness;
        }

        return new HeightfieldData(width, height, cell, 0, 0, heights);
    }
}
