using System;
using Microsoft.Graphics.Canvas;
using PlaySphere.Domain.Visualizer;

namespace PlaySphere.UI.Visualizer;

/// <summary>
/// "Particle Bloom" preset (VIS-09): a field of soft particles that bloom with the audio's energy
/// (PICKLE.md §32/§37). Each particle has a fixed grid position and a per-particle phase; its radius
/// and brightness oscillate with that phase plus a global energy drive, so the whole field throbs in
/// time with the music.
/// </summary>
public sealed class ParticleBloomPreset : IRenderPreset
{
    /// <summary>Particles across the width.</summary>
    public int Columns { get; init; } = 16;

    /// <summary>Particles down the height.</summary>
    public int Rows { get; init; } = 10;

    /// <summary>Phase oscillation speed, in cycles per second.</summary>
    public float PhaseSpeed { get; init; } = 1.8f;

    /// <summary>Maximum particle radius in device-independent pixels.</summary>
    public float MaxRadius { get; init; } = 7f;

    /// <summary>Base particle radius in device-independent pixels.</summary>
    public float BaseRadius { get; init; } = 2f;

    /// <summary>Particle colour.</summary>
    public Windows.UI.Color ParticleColor { get; init; } = ColorFromRgb(0x36, 0xA9, 0xFF);

    public string Name => "Particle Bloom";

    private float _time;

    /// <summary>
    /// Computes the global energy drive (0..1) that scales all particles: a blend of broadband/RMS
    /// energy and transients. Pure so it is unit-testable.
    /// </summary>
    public static float ComputeDrive(AudioAnalysisFrame frame, float peakWeight = 0.5f)
    {
        var bass = frame.BandEnergy.TryGetValue(FrequencyRange.Bass, out var b) ? b : 0f;
        var mid = frame.BandEnergy.TryGetValue(FrequencyRange.Mid, out var m) ? m : 0f;
        var high = frame.BandEnergy.TryGetValue(FrequencyRange.High, out var h) ? h : 0f;

        var broadband = (bass + mid + high) / 3f;
        return Math.Clamp(broadband * (1f - peakWeight) + frame.Peak * peakWeight, 0f, 1f);
    }

    /// <summary>
    /// Computes the per-particle radius given its phase and the global drive (both 0..1). Pure and
    /// unit-testable; larger particles bloom with higher drive.
    /// </summary>
    public static float ParticleRadius(float phase, float drive, float baseRadius, float maxRadius)
    {
        var oscillation = 0.5f + 0.5f * (float)Math.Sin(phase * Math.PI * 2.0);
        return baseRadius + (maxRadius - baseRadius) * Math.Clamp(oscillation * 0.4f + drive * 0.6f, 0f, 1f);
    }

    /// <summary>Computes the per-particle alpha given phase and drive (0..1) and a max byte alpha.</summary>
    public static byte ParticleAlpha(float phase, float drive, byte maxAlpha)
    {
        var oscillation = 0.5f + 0.5f * (float)Math.Sin(phase * Math.PI * 2.0);
        var v = Math.Clamp(oscillation * 0.4f + drive * 0.6f, 0f, 1f);
        return (byte)(v * maxAlpha);
    }

    public void Draw(CanvasDrawingSession session, AudioAnalysisFrame frame, float width, float height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        _time += 1f / 60f;
        var drive = ComputeDrive(frame);

        for (var row = 0; row < Rows; row++)
        {
            for (var col = 0; col < Columns; col++)
            {
                // Deterministic per-cell phase from the cell indices — no allocation, stable field.
                var phase = col * 0.13f + row * 0.37f + _time * PhaseSpeed;
                phase -= (float)Math.Floor(phase);

                var x = Columns <= 1 ? width / 2f : width * (col + 0.5f) / Columns;
                var y = Rows <= 1 ? height / 2f : height * (row + 0.5f) / Rows;

                var radius = ParticleRadius(phase, drive, BaseRadius, MaxRadius);
                if (radius < 0.3f)
                {
                    continue;
                }

                var color = ParticleColor;
                color.A = ParticleAlpha(phase, drive, 0xAF);
                session.FillCircle(x, y, radius, color);
            }
        }
    }

    private static Windows.UI.Color ColorFromRgb(byte r, byte g, byte b)
        => Windows.UI.Color.FromArgb(0xFF, r, g, b);
}
