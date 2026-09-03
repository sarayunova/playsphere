using System.Numerics;
using Microsoft.Graphics.Canvas;
using PlaySphere.Domain.Visualizer;

namespace PlaySphere.UI.Visualizer;

/// <summary>
/// "Neon Wave" preset (VIS-06): a smooth glowing waveform sweeping across the center line. The
/// vertical displacement at each sample follows the log-mapped <see cref="AudioAnalysisFrame.Spectrum"/>
/// magnitudes mapped across the full width (PICKLE.md §32/§37). A translucent wide pass is drawn under
/// a thin bright pass to create a neon glow effect.
/// </summary>
public sealed class NeonWavePreset : IRenderPreset
{
    /// <summary>Number of vertical segments drawn across the width.</summary>
    public int PointCount { get; init; } = 96;

    /// <summary>Peak wave deflection as a fraction of the draw height (0..1).</summary>
    public float MaxAmplitudeFraction { get; init; } = 0.42f;

    /// <summary>Bright stroke width, in device-independent pixels.</summary>
    public float StrokeWidth { get; init; } = 2.5f;

    /// <summary>Glow stroke width (under the bright pass), in device-independent pixels.</summary>
    public float GlowWidth { get; init; } = 9f;

    /// <summary>Base (centre-line) colour of the wave.</summary>
    public Windows.UI.Color WaveColor { get; init; } = ColorFromRgb(0x36, 0xA9, 0xFF);

    /// <summary>Glow colour layered under the bright pass.</summary>
    public Windows.UI.Color GlowColor { get; init; } = ColorFromArgb(0x55, 0x36, 0xA9, 0xFF);

    public string Name => "Neon Wave";

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

        // Lay down the glow underneath, then the bright wave on top.
        for (var pass = 0; pass < 2; pass++)
        {
            var stroke = pass == 0 ? GlowWidth : StrokeWidth;
            var color = pass == 0 ? GlowColor : WaveColor;

            for (var i = 0; i < points - 1; i++)
            {
                var idxA = SourceIndexForSample(points, spectrum.Count, i);
                var idxB = SourceIndexForSample(points, spectrum.Count, i + 1);

                var xA = width * i / (points - 1);
                var xB = width * (i + 1) / (points - 1);
                var yA = centreY - Math.Clamp(spectrum[idxA], 0f, 1f) * maxAmp;
                var yB = centreY - Math.Clamp(spectrum[idxB], 0f, 1f) * maxAmp;

                session.DrawLine(xA, yA, xB, yB, color, stroke);
            }
        }
    }

    private static Windows.UI.Color ColorFromRgb(byte r, byte g, byte b)
        => Windows.UI.Color.FromArgb(0xFF, r, g, b);

    private static Windows.UI.Color ColorFromArgb(byte a, byte r, byte g, byte b)
        => Windows.UI.Color.FromArgb(a, r, g, b);
}
