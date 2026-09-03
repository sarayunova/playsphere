namespace PlaySphere.Media.Export;

/// <summary>
/// Configuration for the ffmpeg clip-export subprocess (PICKLE.md §43: timeout, cancellation,
/// bounded output). Keeps clip exports of untrusted media files bounded.
/// </summary>
public sealed record FfMpegExportOptions
{
    /// <summary>
    /// Absolute path to <c>ffmpeg.exe</c>. If null/empty, the exporter resolves a default from the
    /// app base directory (or a <c>ffmpeg</c> subdirectory), failing softly when none is found.
    /// </summary>
    public string? ExecutablePath { get; init; }

    /// <summary>Per-export hard timeout. An export must never run away.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Cap on how much ffmpeg stderr is buffered for error reporting.</summary>
    public int MaxErrorBytes { get; init; } = 64 * 1024;
}