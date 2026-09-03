namespace PlaySphere.Domain.Visualizer;

/// <summary>
/// FFT window size in samples (PICKLE.md §34 — support 2048 and 4096 points).
/// </summary>
public enum AnalyzerWindow
{
    /// <summary>2048-sample FFT window.</summary>
    Size2048 = 2048,

    /// <summary>4096-sample FFT window.</summary>
    Size4096 = 4096,
}
