using PlaySphere.Domain.Media;
using LibVLCTrack = LibVLCSharp.Shared.MediaTrack;

namespace PlaySphere.Playback.LibVLC;

/// <summary>
/// Maps libVLC <see cref="LibVLCTrack"/> information onto the domain
/// <see cref="MediaMetadata"/> model. Pure projection — no state, no I/O.
/// </summary>
public static class MetadataMapper
{
    public static MediaMetadata ToMetadata(long durationMs, IReadOnlyList<LibVLCTrack> tracks)
    {
        var video = new List<VideoTrackInfo>();
        var audio = new List<AudioTrackInfo>();
        var subtitle = new List<SubtitleTrackInfo>();

        foreach (var track in tracks)
        {
            switch (track.TrackType)
            {
                case LibVLCSharp.Shared.TrackType.Video:
                    video.Add(new VideoTrackInfo(
                        checked((int)track.Id),
                        NullIfEmpty(track.Language),
                        NullIfEmpty(track.Description),
                        FourCcToString(track.Codec),
                        track.Data.Video is { } v ? $"{v.Width}x{v.Height}" : null));
                    break;

                case LibVLCSharp.Shared.TrackType.Audio:
                    audio.Add(new AudioTrackInfo(
                        checked((int)track.Id),
                        NullIfEmpty(track.Language),
                        NullIfEmpty(track.Description),
                        FourCcToString(track.Codec),
                        track.Data.Audio is { } a ? checked((int)a.Rate) : (int?)null,
                        track.Data.Audio is { } a2 ? checked((int)a2.Channels) : (int?)null));
                    break;

                case LibVLCSharp.Shared.TrackType.Text:
                    subtitle.Add(new SubtitleTrackInfo(
                        checked((int)track.Id),
                        NullIfEmpty(track.Language),
                        NullIfEmpty(track.Description),
                        FourCcToString(track.Codec)));
                    break;
            }
        }

        return new MediaMetadata(
            durationMs > 0 ? TimeSpan.FromMilliseconds(durationMs) : TimeSpan.Zero,
            Container: null,
            video,
            audio,
            subtitle);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static string? FourCcToString(uint codec)
    {
        if (codec == 0)
        {
            return null;
        }

        Span<byte> bytes = stackalloc byte[4];
        bytes[0] = (byte)(codec >> 24);
        bytes[1] = (byte)(codec >> 16);
        bytes[2] = (byte)(codec >> 8);
        bytes[3] = (byte)codec;

        var value = System.Text.Encoding.ASCII.GetString(bytes).Trim('\0');
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}