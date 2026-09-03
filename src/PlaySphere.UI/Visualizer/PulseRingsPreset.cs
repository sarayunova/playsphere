using System;
using Microsoft.Graphics.Canvas;
using PlaySphere.Domain.Visualizer;

namespace PlaySphere.UI.Visualizer;

/// <summary>
/// "Pulse Rings" preset (VIS-08): concentric rings that pulse outward with the audio's bass/transient
/// energy (PICKLE.md §32/§37). Ring radii are smoothed over time so the motion eases rather than
/// jitters, and the inner ring responds fastest to transients.
/// </summary>
public sealed class PulseRingsPreset : IRenderPreset
{
    /// <summary>Number of concentric rings.</summary>
    public int RingCount { get; init; } = 5;

    /// <summary>Minimum centre ring radius as a fraction of the smaller dimension.</summary>
    public float BaseRadiusFraction { get; init; } = 0.06f;

    /// <summary>Ring stroke width, in device-independent pixels.</summary>
    public float StrokeWidth { get; init; } = 2f;

    /// <summary>How fast the smoothed ring envelope chases the live energy (0..1).</summary>
    public float Smoothing { get; init; } = 0.6f;

    /// <summary>Peak ring radius (beyond which rings space out) as a fraction of the smaller dimension.</summary>
    public float MaxRadiusFraction { get; init; } = 0.5f;

    /// <summary>Inner-ring accent colour.</summary>
    public Windows.UI.Color InnerColor { get; init; } = ColorFromRgb(0xEE, 0x49, 0x99);

    /// <summary>Outer-ring accent colour.</summary>
    public Windows.UI.Color OuterColor { get; init; } = ColorFromRgb(0x36, 0xA9, 0xFF);

    public string Name => "Pulse Rings";

    private float _smoothed = 0f;

    /// <summary>
    /// Computes the raw envelope (0..1) driving the ring pulse: a blend of the live RMS and the
    /// transient energy, biased toward bass. Pure so it is unit-testable.
    /// </summary>
    public static float ComputeEnvelope(AudioAnalysisFrame frame, float bassWeight = 0.6f)
    {
        var bass = frame.BandEnergy.TryGetValue(FrequencyRange.Bass, out var b) ? b : 0f;
        var mid = frame.BandEnergy.TryGetValue(FrequencyRange.Mid, out var m) ? m : 0f;
        var high = frame.BandEnergy.TryGetValue(FrequencyRange.High, out var h) ? h : 0f;

        var broadband = (bass * bassWeight + (mid + high) * (1f - bassWeight)) * 0.5f;
        var transient = Math.Clamp(frame.TransientEnergy, 0f, 1f);
        return Math.Clamp(broadband * 0.7f + transient * 0.3f + frame.Rms * 0.3f, 0f, 1f);
    }

    public void Draw(CanvasDrawingSession session, AudioAnalysisFrame frame, float width, float height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var envelope = ComputeEnvelope(frame);
        _smoothed = _smoothed + (envelope - _smoothed) * Math.Clamp(Smoothing, 0f, 1f);
        var pulse = _smoothed;

        var cx = width / 2f;
        var cy = height / 2f;
        var span = Math.Min(width, height);
        var baseRadius = span * BaseRadiusFraction;
        var maxRadius = span * MaxRadiusFraction;

        for (var i = 0; i < RingCount; i++)
        {
            var t = RingCount == 1 ? 0f : i / (float)(RingCount - 1);
            var radius = baseRadius + t * (maxRadius - baseRadius) + pulse * baseRadius * 0.9f;

            // Fade the outer rings toward the outer colour and lower alpha.
            var color = LerpColor(InnerColor, OuterColor, t);
            var alpha = (byte)(0x60 + t * 0x9F);
            color.A = alpha;

            session.DrawCircle(cx, cy, radius, color, StrokeWidth);
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
