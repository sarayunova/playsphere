namespace PlaySphere.Domain.Media;

/// <summary>
/// Technical metadata for a cataloged media file, produced by FFprobe (PICKLE.md §23). Distinct
/// from <see cref="MediaMetadata"/>, which is the richer per-playback stream breakdown fed to the
/// inspector UI — this is the lighter summary persisted on <c>LibraryMediaItem</c>: enough to title
/// cards, sort and show a technical summary without holding all streams in memory.
/// </summary>
public sealed record MediaProbeResult(
    TimeSpan Duration,
    int? Width,
    int? Height,
    string? Resolution,
    string? Codec,
    double? FrameRate,
    long? BitrateKbps,
    int AudioTrackCount,
    int SubtitleTrackCount)
{
    public static MediaProbeResult Empty { get; } =
        new(TimeSpan.Zero, null, null, null, null, null, null, 0, 0);
}
