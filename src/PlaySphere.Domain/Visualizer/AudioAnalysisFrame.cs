namespace PlaySphere.Domain.Visualizer;

/// <summary>
/// Immutable result of a single audio-analysis pass (PICKLE.md §33). One of these is produced
/// by the analyser and handed to the renderer via the latest-frame bounded channel so the
/// playback thread never blocks on the visualizer (gate for Phase 6).
/// </summary>
public sealed record AudioAnalysisFrame
{
    /// <summary>Sample rate of the input PCM in Hz (never assumed 44.1 kHz — §34).</summary>
    public required int SampleRate { get; init; }

    /// <summary>Number of input channels (1 = mono, 2 = stereo).</summary>
    public required int ChannelCount { get; init; }

    /// <summary>Log-mapped spectral magnitude, one value per spectrum band. Band 0 is lowest freqs.</summary>
    public required IReadOnlyList<float> Spectrum { get; init; }

    /// <summary>Overall root-mean-square level, normalised 0..1.</summary>
    public required float Rms { get; init; }

    /// <summary>Peak sample magnitude over the window, normalised 0..1.</summary>
    public required float Peak { get; init; }

    /// <summary>Per-range energy, normalised 0..1 against the strongest range in this frame.</summary>
    public required IReadOnlyDictionary<FrequencyRange, float> BandEnergy { get; init; }

    /// <summary>Transient energy: how much the window's energy jumped versus the previous frame, 0..1.</summary>
    public required float TransientEnergy { get; init; }
}
