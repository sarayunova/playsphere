using System;
using Microsoft.Graphics.Canvas;
using PlaySphere.Domain.Visualizer;

namespace PlaySphere.UI.Visualizer;

/// <summary>
/// "Minimal Spectrum" preset: a clean, understated bar spectrum with fewer, thinner, uniformly
/// spaced bars and a single flat accent colour — a quiet, high-fidelity alternative to the busier
/// presets (PICKLE.md §32/§37). No per-frame allocation; only the session's Fill primitives.
/// </summary>
public sealed class MinimalSpectrumPreset : IRenderPreset
{
    /// <summary>Number of bars drawn across the width.</summary>
    public int BarCount { get; init; } = 32;

    /// <summary>Maximum bar height as a fraction of the draw height (0..1).</summary>
    public float MaxBarHeightFraction { get; init; } = 0.9f;

    /// <summary>Gap between bars, in device-independent pixels.</summary>
    public float BarGap { get; init; } = 3f;

    /// <summary>Bar corner radius, in device-independent pixels.</summary>
    public float CornerRadius { get; init; } = 1.5f;

    /// <summary>Single flat accent colour.</summary>
    public Windows.UI.Color BarColor { get; init; } = ColorFromRgb(0x35, 0xD0, 0x7F);

    public string Name => "Minimal Spectrum";

    /// <summary>
    /// Maps a bar (0..barCount-1) down to the spectrum band it represents, covering the band range
    /// proportionally (mirrors <see cref="SpectrumFlowPreset.SourceIndexForBar"/>).
    /// </summary>
    public static int SourceIndexForBar(int barCount, int spectrumCount, int bar)
    {
        if (barCount < 1 || spectrumCount < 1 || bar < 0)
        {
            return 0;
        }

        return (int)Math.Min(spectrumCount - 1, (long)bar * spectrumCount / Math.Max(1, barCount));
    }

    /// <summary>
    /// Applies a gentle perceptual curve so lower bands are slightly boosted without saturating —
    /// keeps the minimal look readable. Pure and bounded 0..1.
    /// </summary>
    public static float CurveEnergy(float raw)
    {
        var v = Math.Clamp(raw, 0f, 1f);
        return Math.Clamp(0.55f * v + 0.45f * v * v, 0f, 1f);
    }

    public void Draw(CanvasDrawingSession session, AudioAnalysisFrame frame, float width, float height)
    {
        if (width <= 0 || height <= 0 || frame.Spectrum.Count == 0)
        {
            return;
        }

        var bars = BarCount;
        var gap = BarGap;
        var barSpace = width / bars;
        var barWidth = Math.Max(0.5f, barSpace - gap);
        var maxBarHeight = height * MaxBarHeightFraction;
        var baseline = height;
        var spectrum = frame.Spectrum;

        for (var i = 0; i < bars; i++)
        {
            var sourceIndex = SourceIndexForBar(bars, spectrum.Count, i);
            var h = CurveEnergy(spectrum[sourceIndex]) * maxBarHeight;
            if (h < 0.5f)
            {
                continue;
            }

            var x = i * barSpace;
            session.FillRoundedRectangle(
                new Windows.Foundation.Rect(x, baseline - h, barWidth, h),
                CornerRadius, CornerRadius, BarColor);
        }
    }

    private static Windows.UI.Color ColorFromRgb(byte r, byte g, byte b)
        => Windows.UI.Color.FromArgb(0xFF, r, g, b);
}
