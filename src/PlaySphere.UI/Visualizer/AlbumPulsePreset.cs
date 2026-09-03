using System;
using Microsoft.Graphics.Canvas;
using PlaySphere.Domain.Visualizer;

namespace PlaySphere.UI.Visualizer;

/// <summary>
/// "Album Pulse" preset: an album-art-style central burst. A rotating set of radial rays fan out
/// from the centre and pulse in length with the audio's band energy, layered over a soft concentric
/// glow ring — reading like an album cover coming to life (PICKLE.md §32/§37).
/// </summary>
public sealed class AlbumPulsePreset : IRenderPreset
{
    /// <summary>Number of radial rays.</summary>
    public int RayCount { get; init; } = 60;

    /// <summary>Central glow rings.</summary>
    public int GlowRingCount { get; init; } = 3;

    /// <summary>Minimum ray base length as a fraction of the smaller dimension.</summary>
    public float BaseRadiusFraction { get; init; } = 0.12f;

    /// <summary>Maximum ray length as a fraction of the smaller dimension.</summary>
    public float MaxRadiusFraction { get; init; } = 0.5f;

    /// <summary>Ray stroke width, in device-independent pixels.</summary>
    public float RayWidth { get; init; } = 3f;

    /// <summary>Central glow ring stroke width, in device-independent pixels.</summary>
    public float GlowWidth { get; init; } = 5f;

    /// <summary>Cool accent colour for the rays.</summary>
    public Windows.UI.Color RayColor { get; init; } = ColorFromRgb(0x36, 0xA9, 0xFF);

    /// <summary>Warm accent colour for the central glow rings.</summary>
    public Windows.UI.Color GlowColor { get; init; } = ColorFromRgb(0xEE, 0x49, 0x99);

    public string Name => "Album Pulse";

    /// <summary>
    /// Rotation speed in radians per second: 0 keeps a static sunburst; a small positive value
    /// makes the rays slowly precess.
    /// </summary>
    public float RotationSpeed { get; init; } = 0.4f;

    private float _time;

    /// <summary>
    /// Computes the audio drive (0..1) that scales the rays: a mix of the broadband/RMS energy and
    /// the bass, biased toward per-frame peak transient motion. Pure so it is unit-testable.
    /// </summary>
    public static float ComputeDrive(AudioAnalysisFrame frame, float bassWeight = 0.5f)
    {
        var bass = frame.BandEnergy.TryGetValue(FrequencyRange.Bass, out var b) ? b : 0f;
        var mid = frame.BandEnergy.TryGetValue(FrequencyRange.Mid, out var m) ? m : 0f;
        var high = frame.BandEnergy.TryGetValue(FrequencyRange.High, out var h) ? h : 0f;

        var broadband = (bass + mid + high) / 3f;
        var rhythmic = bass * bassWeight + mid * (1f - bassWeight);
        return Math.Clamp(broadband * 0.5f + rhythmic * 0.3f + frame.Rms * 0.2f, 0f, 1f);
    }

    /// <summary>
    /// Computes one ray's length given its index, the drive (0..1) and the geometry. Outer rays are
    /// slightly longer so the burst has a starburst silhouette. Pure and unit-testable.
    /// </summary>
    public static float RayLength(int ray, int rayCount, float drive, float baseRadius, float maxRadius, float pulseDepth)
    {
        var spread = rayCount <= 1 ? 0f : (ray % 4) / 4f;
        var depth = drive * pulseDepth + spread * 0.15f;
        return baseRadius + (maxRadius - baseRadius) * Math.Clamp(depth, 0f, 1f);
    }

    public void Draw(CanvasDrawingSession session, AudioAnalysisFrame frame, float width, float height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        _time += 1f / 60f;
        var cx = width / 2f;
        var cy = height / 2f;
        var span = Math.Min(width, height);
        var baseRadius = span * BaseRadiusFraction;
        var maxRadius = span * MaxRadiusFraction;
        var drive = ComputeDrive(frame);
        var rotation = _time * RotationSpeed;

        // Central glow rings, pulsing gently with the drive.
        for (var g = 0; g < GlowRingCount; g++)
        {
            var t = GlowRingCount == 1 ? 0f : g / (float)(GlowRingCount - 1);
            var radius = baseRadius * 0.6f + baseRadius * 0.4f * t + drive * baseRadius * 0.25f;
            var color = GlowColor;
            color.A = (byte)(0x48 + t * 0x58);
            session.DrawCircle(cx, cy, radius, color, GlowWidth);
        }

        // Radial rays.
        for (var ray = 0; ray < RayCount; ray++)
        {
            var angle = -Math.PI / 2.0 + rotation + 2.0 * Math.PI * ray / RayCount;
            var dirX = (float)Math.Cos(angle);
            var dirY = (float)Math.Sin(angle);

            var length = RayLength(ray, RayCount, drive, baseRadius, maxRadius, 0.72f);
            var x0 = cx + dirX * baseRadius * 0.5f;
            var y0 = cy + dirY * baseRadius * 0.5f;
            var x1 = cx + dirX * length;
            var y1 = cy + dirY * length;

            var color = RayColor;
            color.A = 0xE0;
            session.DrawLine(x0, y0, x1, y1, color, RayWidth);
        }
    }

    private static Windows.UI.Color ColorFromRgb(byte r, byte g, byte b)
        => Windows.UI.Color.FromArgb(0xFF, r, g, b);
}
