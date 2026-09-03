using System;
using Microsoft.Graphics.Canvas;
using PlaySphere.Domain.Visualizer;

namespace PlaySphere.UI.Visualizer;

/// <summary>
/// "Prism Tunnel" preset: a radial spectrum rendered as a concentric tunnel of prism bands. Each of
/// a few concentric rings is divided into radial segments whose short strokes pulse outward with the
/// spectrum, producing a refracting "prism" effect toward the viewer (PICKLE.md §32/§37).
/// </summary>
public sealed class PrismTunnelPreset : IRenderPreset
{
    /// <summary>Number of radial segments around each ring.</summary>
    public int SegmentCount { get; init; } = 72;

    /// <summary>Number of concentric rings.</summary>
    public int RingCount { get; init; } = 4;

    /// <summary>Inner ring radius as a fraction of the smaller dimension.</summary>
    public float BaseRadiusFraction { get; init; } = 0.08f;

    /// <summary>Outer ring radius as a fraction of the smaller dimension.</summary>
    public float MaxRadiusFraction { get; init; } = 0.46f;

    /// <summary>Segment stroke width, in device-independent pixels.</summary>
    public float StrokeWidth { get; init; } = 4f;

    /// <summary>Inner (cool) colour.</summary>
    public Windows.UI.Color InnerColor { get; init; } = ColorFromRgb(0x36, 0xA9, 0xFF);

    /// <summary>Outer (warm) colour.</summary>
    public Windows.UI.Color OuterColor { get; init; } = ColorFromRgb(0xEE, 0x49, 0x99);

    public string Name => "Prism Tunnel";

    /// <summary>
    /// Maps a radial segment (0..segmentCount-1) to the spectrum band it represents, covering the
    /// band range proportionally (mirrors <see cref="SpectrumFlowPreset.SourceIndexForBar"/>).
    /// </summary>
    public static int SourceIndexForSegment(int segmentCount, int spectrumCount, int segment)
    {
        if (segmentCount < 1 || spectrumCount < 1 || segment < 0)
        {
            return 0;
        }

        return (int)Math.Min(spectrumCount - 1, (long)segment * spectrumCount / Math.Max(1, segmentCount));
    }

    /// <summary>
    /// Computes the per-ring radial pulse length: rings nearer the centre pulse most with the energy,
    /// and the whole band pushes outward. Pure so it is unit-testable.
    /// </summary>
    public static float RingPulse(float energy, float ringT, float maxPulseFraction)
    {
        var weight = 1f - ringT * 0.5f;
        return Math.Clamp(energy, 0f, 1f) * maxPulseFraction * weight;
    }

    public void Draw(CanvasDrawingSession session, AudioAnalysisFrame frame, float width, float height)
    {
        if (width <= 0 || height <= 0 || frame.Spectrum.Count == 0)
        {
            return;
        }

        var cx = width / 2f;
        var cy = height / 2f;
        var span = Math.Min(width, height);
        var baseRadius = span * BaseRadiusFraction;
        var maxRadius = span * MaxRadiusFraction;
        var spectrum = frame.Spectrum;
        var segments = SegmentCount;

        for (var ring = 0; ring < RingCount; ring++)
        {
            var ringT = RingCount == 1 ? 0f : ring / (float)(RingCount - 1);
            var ringRadius = baseRadius + ringT * (maxRadius - baseRadius);
            var color = LerpColor(InnerColor, OuterColor, ringT);

            for (var s = 0; s < segments; s++)
            {
                var angle = -Math.PI / 2.0 + 2.0 * Math.PI * s / segments;
                var dirX = (float)Math.Cos(angle);
                var dirY = (float)Math.Sin(angle);

                var sourceIndex = SourceIndexForSegment(segments, spectrum.Count, s);
                var pulse = RingPulse(spectrum[sourceIndex], ringT, 0.28f);

                var x0 = cx + dirX * ringRadius;
                var y0 = cy + dirY * ringRadius;
                var x1 = cx + dirX * (ringRadius + pulse * maxRadius);
                var y1 = cy + dirY * (ringRadius + pulse * maxRadius);

                session.DrawLine(x0, y0, x1, y1, color, StrokeWidth);
            }
        }
    }

    private static Windows.UI.Color LerpColor(Windows.UI.Color a, Windows.UI.Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Windows.UI.Color.FromArgb(
            0xFF,
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    private static Windows.UI.Color ColorFromRgb(byte r, byte g, byte b)
        => Windows.UI.Color.FromArgb(0xFF, r, g, b);
}
