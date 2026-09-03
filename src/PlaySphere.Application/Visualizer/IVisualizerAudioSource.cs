namespace PlaySphere.Application.Visualizer;

/// <summary>
/// Source of decoded interleaved PCM samples for the visualizer (PICKLE.md §33).
/// This is the boundary between the decoders (e.g. libVLC) and the DSP analyser; the UI and
/// the analyser depend only on this contract, never on the decoder. It feeds the analyser
/// regardless of channel layout — the analyser is told the format per batch.
/// </summary>
public interface IVisualizerAudioSource
{
    /// <summary>
    /// Starts the source delivering samples. For decoder-fed sources (e.g. the engine PCM tap) this is
    /// a no-op because samples arrive via <see cref="PushSamples"/>; for capture-based sources (e.g.
    /// WASAPI device-loopback, ADR-0009) this begins capturing. Must be safe to call more than once.
    /// </summary>
    void Start();

    /// <summary>
    /// Pushes a batch of interleaved float PCM samples (range roughly −1..1) into the source.
    /// </summary>
    /// <param name="samples">Interleaved samples, in the order L,R,L,R,… for stereo, or mono otherwise.</param>
    /// <param name="sampleRate">Sample rate of <paramref name="samples"/> in Hz (never assumed 44.1 kHz).</param>
    /// <param name="channelCount">Channel count of the interleaved buffer (1 or 2).</param>
    void PushSamples(ReadOnlySpan<float> samples, int sampleRate, int channelCount);

    /// <summary>Stops the stream and discards any buffered samples.</summary>
    void Stop();
}
