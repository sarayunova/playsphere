using Microsoft.Graphics.Canvas;
using PlaySphere.Domain.Visualizer;

namespace PlaySphere.UI.Visualizer;

/// <summary>
/// "Spectrum Flow" preset (VIS-05): the log-mapped <see cref="AudioAnalysisFrame.Spectrum"/>
/// rendered as a row of vertical bars sweeping up from the baseline in the accent gradient.
/// The bar heights follow the instantaneous frequency magnitudes (PICKLE.md §32/§37).
/// </summary>
public sealed class SpectrumFlowPreset : IRenderPreset
{
    /// <summary>Number of bars drawn across the width.</summary>
    public int BarCount { get; init; } = 48;

    /// <summary>Maximum bar height as a fraction of the draw height (0..1).</summary>
    public float MaxBarHeightFraction { get; init; } = 0.92f;

    /// <summary>Vertical gap between bars, in device-independent pixels.</summary>
    public float BarGap { get; init; } = 2.5f;

    /// <summary>Top accent colour of the bar gradient.</summary>
    public Windows.UI.Color TopColor { get; init; } = ColorFromRgb(0xEE, 0x49, 0x99);

    /// <summary>Bottom accent colour of the bar gradient.</summary>
    public Windows.UI.Color BottomColor { get; init; } = ColorFromRgb(0x36, 0xA9, 0xFF);

    public string Name => "Spectrum Flow";

    /// <summary>
    /// Maps a bar (0..barCount-1) down to the spectrum sample it represents, so the full band
    /// range is covered proportionally even when the spectrum length differs from the bar count.
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

        var bars = BarCount;
        var gap = BarGap;
        var barSpace = width / bars;
        var barWidth = Math.Max(0.5f, barSpace - gap);
        var maxBarHeight = height * MaxBarHeightFraction;
        var baseline = height;

        // Bottom → top (cyan → magenta) vertical gradient, matching the app's accent palette.
        // Drawn allocation-free: each bar gets a solid colour interpolated by its height fraction,
        // rather than allocating a per-frame CanvasLinearGradientBrush (a documented native-AV
        // hazard for Win2D animated draws on some GPUs/drivers).
        var spectrum = frame.Spectrum;
        for (var i = 0; i < bars; i++)
        {
            var sourceIndex = SourceIndexForBar(bars, spectrum.Count, i);
            var energy = Math.Clamp(spectrum[sourceIndex], 0f, 1f);
            var h = energy * maxBarHeight;
            if (h < 0.5f)
            {
                continue;
            }

            var x = i * barSpace;
            var color = LerpColor(BottomColor, TopColor, Math.Clamp(h / maxBarHeight, 0f, 1f));
            session.FillRoundedRectangle(new Windows.Foundation.Rect(x, baseline - h, barWidth, h), 2f, 2f, color);
        }
    }

    /// <summary>Interpolates between two colours by a 0..1 blend factor (pure, testable).</summary>
    public static Windows.UI.Color LerpColor(Windows.UI.Color a, Windows.UI.Color b, float t)
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
