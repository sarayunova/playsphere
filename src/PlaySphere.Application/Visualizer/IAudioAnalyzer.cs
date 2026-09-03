using PlaySphere.Domain.Visualizer;

namespace PlaySphere.Application.Visualizer;

/// <summary>
/// Analyses decoded PCM into a renderable <see cref="AudioAnalysisFrame"/> (PICKLE.md §33).
/// Pure DSP — no rendering, no I/O — so it is fully unit-testable with synthetic signals (§37).
/// Implementations must never block on the caller's audio thread for rendering.
/// </summary>
public interface IAudioAnalyzer
{
    /// <summary>
    /// Analyses one window of interleaved float PCM into an <see cref="AudioAnalysisFrame"/>.
    /// </summary>
    /// <param name="samples">Interleaved samples (L,R,L,R,… for stereo).</param>
    /// <param name="sampleRate">Sample rate in Hz.</param>
    /// <param name="channelCount">1 (mono) or 2 (stereo).</param>
    /// <param name="previousRms">RMS of the previous frame, used to compute transient energy.</param>
    /// <returns>An immutable analysis frame.</returns>
    AudioAnalysisFrame Analyze(ReadOnlySpan<float> samples, int sampleRate, int channelCount, float previousRms);
}
