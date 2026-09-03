using System;
using Microsoft.Graphics.Canvas;
using PlaySphere.Domain.Visualizer;

namespace PlaySphere.UI.Visualizer;

/// <summary>
/// "Orbit Spectrum" preset (VIS-07): the log-mapped spectrum rendered as radial bars orbiting a
/// centre point, like a circular equaliser (PICKLE.md §32/§37). Each spectrum band becomes a line
/// from the inner ring out to a radius proportional to its magnitude.
/// </summary>
public sealed class OrbitSpectrumPreset : IRenderPreset
{
    /// <summary>Number of radial bars around the circle.</summary>
    public int BarCount { get; init; } = 64;

    /// <summary>Minimum (base) ring radius as a fraction of the smaller dimension.</summary>
    public float BaseRadiusFraction { get; init; } = 0.14f;

    /// <summary>Maximum outer ring radius as a fraction of the smaller dimension.</summary>
    public float MaxRadiusFraction { get; init; } = 0.48f;

    /// <summary>Bar stroke width, in device-independent pixels.</summary>
    public float StrokeWidth { get; init; } = 3f;

    /// <summary>Accent colour of the bars.</summary>
    public Windows.UI.Color BarColor { get; init; } = ColorFromRgb(0xEE, 0x49, 0x99);

    public string Name => "Orbit Spectrum";

    /// <summary>
    /// Maps a radial bar (0..barCount-1) to the spectrum band it represents, covering the band
    /// range proportionally (mirrors <see cref="SpectrumFlowPreset.SourceIndexForBar"/>).
    /// </summary>
    public static int SourceIndexForBar(int barCount, int spectrumCount, int bar)
    {
        if (barCount < 1 || spectrumCount < 1 || bar < 0)
        {
            return 0;
        }

        return (int)Math.Min(spectrumCount - 1, (long)bar * spectrumCount / Math.Max(1, barCount));
    }

    public void Draw(CanvasDrawingSession session, AudioAnalysisFrame frame, float width, float height)
    {
        if (width <= 0 || height <= 0 || frame.Spectrum.Count == 0)
        {
            return;
        }

        var cx = width / 2f;
        var cy = height / 2f;
        var ring = Math.Min(width, height);
        var baseRadius = ring * BaseRadiusFraction;
        var maxRadius = ring * MaxRadiusFraction;
        var spectrum = frame.Spectrum;

        var bars = BarCount;
        for (var i = 0; i < bars; i++)
        {
            var angle = -Math.PI / 2.0 + 2.0 * Math.PI * i / bars;
            var dirX = (float)Math.Cos(angle);
            var dirY = (float)Math.Sin(angle);

            var sourceIndex = SourceIndexForBar(bars, spectrum.Count, i);
            var energy = Math.Clamp(spectrum[sourceIndex], 0f, 1f);
            var outerRadius = baseRadius + energy * (maxRadius - baseRadius);

            var x0 = cx + dirX * baseRadius;
            var y0 = cy + dirY * baseRadius;
            var x1 = cx + dirX * outerRadius;
            var y1 = cy + dirY * outerRadius;

            session.DrawLine(x0, y0, x1, y1, BarColor, StrokeWidth);
        }
    }

    private static Windows.UI.Color ColorFromRgb(byte r, byte g, byte b)
        => Windows.UI.Color.FromArgb(0xFF, r, g, b);
}
