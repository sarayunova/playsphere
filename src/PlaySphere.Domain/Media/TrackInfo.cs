namespace PlaySphere.Domain.Media;

/// <summary>
/// Audio/video/subtitle stream kinds contained in a media file.
/// </summary>
public enum TrackType
{
    Video,
    Audio,
    Subtitle,
}

public sealed record VideoTrackInfo(
    int Id,
    string? Language,
    string? Name,
    string? Codec,
    string? Resolution);

public sealed record AudioTrackInfo(
    int Id,
    string? Language,
    string? Name,
    string? Codec,
    int? SampleRate,
    int? Channels);

public sealed record SubtitleTrackInfo(
    int Id,
    string? Language,
    string? Name,
    string? Encoding);

/// <summary>
/// Technical metadata for a loaded media item. Populated from media probing/parsing;
/// tracked per-media, not per information that changes during playback.
/// </summary>
public sealed record MediaMetadata(
    TimeSpan Duration,
    string? Container,
    IReadOnlyList<VideoTrackInfo> VideoTracks,
    IReadOnlyList<AudioTrackInfo> AudioTracks,
    IReadOnlyList<SubtitleTrackInfo> SubtitleTracks)
{
    public static MediaMetadata Empty { get; } =
        new(TimeSpan.Zero, null, Array.Empty<VideoTrackInfo>(), Array.Empty<AudioTrackInfo>(), Array.Empty<SubtitleTrackInfo>());
}