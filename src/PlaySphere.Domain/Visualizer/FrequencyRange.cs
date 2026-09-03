namespace PlaySphere.Domain.Visualizer;

/// <summary>
/// Broad frequency ranges used for the visualizer's bass/mid/high energy split
/// (PICKLE.md §34). The analyser accumulates FFT bin energy within each range.
/// </summary>
public enum FrequencyRange
{
    /// <summary>Low-frequency energy (~20–200 Hz).</summary>
    Bass,

    /// <summary>Mid-frequency energy (~200 Hz–4 kHz).</summary>
    Mid,

    /// <summary>High-frequency energy (~4 kHz–20 kHz).</summary>
    High,
}
