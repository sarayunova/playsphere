namespace PlaySphere.Media.Probe;

/// <summary>
/// Configuration for the FFprobe subprocess (PICKLE.md §43: FFmpeg timeout, process
/// cancellation, bounded output). Used to keep probing of untrusted media files bounded.
/// </summary>
public sealed record FfProbeOptions
{
    /// <summary>
    /// Absolute path to <c>ffprobe.exe</c>. If null/empty, the probe resolves a default from the
    /// app base directory (or a <c>ffmpeg</c> subdirectory), logging a warning when none is found.
    /// </summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Per-probe hard timeout. Probing must never hang a scan.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Cap on how much ffprobe stdout is buffered before a probe is abandoned.</summary>
    public int MaxOutputBytes { get; init; } = 512 * 1024;
}
