using System;
using Microsoft.Graphics.Canvas;
using PlaySphere.Domain.Visualizer;

namespace PlaySphere.UI.Visualizer;

/// <summary>
/// "Aurora Ribbon" preset: a flowing translucent ribbon waveform that sweeps across the screen,
/// layered in the accent blue→purple→magenta gradient. The upper edge follows the spectrum
/// magnitudes while the lower edge mirrors them, so the ribbon reads as a thick glowing band that
/// undulates with the music (PICKLE.md §32/§37). Only the session's color-taking Draw primitives
/// are used, with no per-frame allocation.
/// </summary>
public sealed class AuroraRibbonPreset : IRenderPreset
{
    /// <summary>Number of vertical segments drawn across the width.</summary>
    public int PointCount { get; init; } = 64;

    /// <summary>Peak ribbon deflection as a fraction of the draw height (0..1).</summary>
    public float MaxAmplitudeFraction { get; init; } = 0.34f;

    /// <summary>Ribbon stroke width (the thick translucent main pass), in device-independent pixels.</summary>
    public float StrokeWidth { get; init; } = 10f;

    /// <summary>Bright upper-edge stroke width, in device-independent pixels.</summary>
    public float EdgeStrokeWidth { get; init; } = 1.6f;

    /// <summary>Start colour of the ribbon gradient (cool).</summary>
    public Windows.UI.Color StartColor { get; init; } = ColorFromRgb(0x36, 0xA9, 0xFF);

    /// <summary>Mid colour of the ribbon gradient (purple).</summary>
    public Windows.UI.Color MidColor { get; init; } = ColorFromRgb(0x8B, 0x5C, 0xF6);

    /// <summary>End colour of the ribbon gradient (warm).</summary>
    public Windows.UI.Color EndColor { get; init; } = ColorFromRgb(0xEC, 0x48, 0x99);

    public string Name => "Aurora Ribbon";

    /// <summary>
    /// Maps a horizontal sample index (0..pointCount-1) to the spectrum band it represents, covering
    /// the band range proportionally (mirrors <see cref="SpectrumFlowPreset.SourceIndexForBar"/>).
    /// </summary>
    public static int SourceIndexForSample(int pointCount, int spectrumCount, int sample)
    {
        if (pointCount < 1 || spectrumCount < 1 || sample < 0)
        {
            return 0;
        }

        return (int)Math.Min(spectrumCount - 1, (long)sample * spectrumCount / Math.Max(1, pointCount));
    }

    /// <summary>
    /// Computes the ribbon's vertical offset at a given horizontal sample: the mirrored band
    /// energy displaced from the centre line (positive = above). Pure so it is unit-testable.
    /// </summary>
    public static float RibbonOffset(float energy, float maxAmplitude)
    {
        return Math.Clamp(energy, 0f, 1f) * maxAmplitude;
    }

    public void Draw(CanvasDrawingSession session, AudioAnalysisFrame frame, float width, float height)
    {
        if (width <= 0 || height <= 0 || frame.Spectrum.Count == 0)
        {
            return;
        }

        var points = PointCount;
        var maxAmp = height * MaxAmplitudeFraction;
        var centreY = height / 2f;
        var spectrum = frame.Spectrum;

        // Three passes: the wide translucent ribbon body, then a thin bright upper edge, then a
        // thin bright lower edge — giving the "aurora band" glow without per-frame allocations.
        for (var pass = 0; pass < 3; pass++)
        {
            var stroke = pass == 0 ? StrokeWidth : EdgeStrokeWidth;
            var alphaBase = (byte)(pass == 0 ? 0x5A : 0xE0);

            for (var i = 0; i < points - 1; i++)
            {
                var idxA = SourceIndexForSample(points, spectrum.Count, i);
                var idxB = SourceIndexForSample(points, spectrum.Count, i + 1);

                var xA = width * i / (points - 1);
                var xB = width * (i + 1) / (points - 1);

                var offA = RibbonOffset(spectrum[idxA], maxAmp);
                var offB = RibbonOffset(spectrum[idxB], maxAmp);

                // Upper edge follows the magnitude, lower edge mirrors it.
                var yTopA = centreY - offA;
                var yTopB = centreY - offB;
                var yBottomA = centreY + offA;
                var yBottomB = centreY + offB;

                if (pass == 0)
                {
                    // Thick translucent ribbon: average the upper and lower deflections onto a
                    // single centre band for a broad glow.
                    var yMidA = (yTopA + yBottomA) / 2f;
                    var yMidB = (yTopB + yBottomB) / 2f;
                    var color = PaletteColor(i, points - 1, alphaBase);
                    session.DrawLine(xA, yMidA, xB, yMidB, color, stroke);
                }
                else
                {
                    var yA = pass == 1 ? yTopA : yBottomA;
                    var yB = pass == 1 ? yTopB : yBottomB;
                    var color = PaletteColor(i, points - 1, alphaBase);
                    session.DrawLine(xA, yA, xB, yB, color, stroke);
                }
            }
        }
    }

    /// <summary>Gradient colour at a 0..1 position across blue→purple→magenta (pure, testable).</summary>
    public static Windows.UI.Color PaletteColor(float t, float count, byte alpha)
    {
        var p = count <= 1f ? 0f : Math.Clamp(t / count, 0f, 1f);
        Windows.UI.Color color;
        if (p < 0.5f)
        {
            color = LerpColor(StartColorDefault, MidColorDefault, p * 2f);
        }
        else
        {
            color = LerpColor(MidColorDefault, EndColorDefault, (p - 0.5f) * 2f);
        }
        color.A = alpha;
        return color;
    }

    private static Windows.UI.Color StartColorDefault => ColorFromRgb(0x36, 0xA9, 0xFF);
    private static Windows.UI.Color MidColorDefault => ColorFromRgb(0x8B, 0x5C, 0xF6);
    private static Windows.UI.Color EndColorDefault => ColorFromRgb(0xEC, 0x48, 0x99);

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
