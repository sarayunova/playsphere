namespace PlaySphere.Domain.Media;

/// <summary>
/// The bounded catalog of conversion targets (PICKLE.md §29 — remux, video transcode, video-to-audio
/// and audio conversion). Pure data: the ffmpeg argument slices are used verbatim as a structured
/// array by the converter; nothing here performs I/O.
/// </summary>
public static class ConversionPresets
{
    /// <summary>Maps the first video and first audio stream (both optional) so either is populated.</summary>
    private static readonly string[] MapVideoAudio = ["-map", "0:v:0?", "-map", "0:a:0?"];

    /// <summary>Drops all video and maps only the first audio stream (optional); used by audio-only presets.</summary>
    private static readonly string[] MapAudioOnly = ["-vn", "-map", "0:a:0?"];

    public static IReadOnlyList<ConversionPreset> All { get; } =
    [
        new ConversionPreset(
            "mp4-h264",
            "MP4 (H.264 / AAC)",
            "Widely compatible MPEG-4 video (H.264) with AAC audio — best for phones, TVs and sharing.",
            ".mp4",
            [.. MapVideoAudio, "-c:v", "libx264", "-preset", "medium", "-crf", "23", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart"]),
        new ConversionPreset(
            "mp4-h265",
            "MP4 (HEVC / AAC)",
            "Efficient HEVC video with AAC — roughly half the bitrate of H.264 at the same quality (fewer older devices play it).",
            ".mp4",
            [.. MapVideoAudio, "-c:v", "libx265", "-preset", "medium", "-crf", "28", "-c:a", "aac", "-b:a", "192k", "-movflags", "+faststart"]),
        new ConversionPreset(
            "mkv-h264",
            "MKV (H.264 / AAC)",
            "H.264 video with AAC in the flexible Matroska container.",
            ".mkv",
            [.. MapVideoAudio, "-c:v", "libx264", "-preset", "medium", "-crf", "23", "-c:a", "aac", "-b:a", "192k"]),
        new ConversionPreset(
            "mkv-h265",
            "MKV (HEVC / AAC)",
            "HEVC video with AAC in the Matroska container — best quality-per-size for desktop playback.",
            ".mkv",
            [.. MapVideoAudio, "-c:v", "libx265", "-preset", "medium", "-crf", "28", "-c:a", "aac", "-b:a", "192k"]),
        new ConversionPreset(
            "mkv-remux",
            "MKV (remux, stream copy)",
            "Rewrap every existing stream into MKV with no re-encoding — fast, lossless container change.",
            ".mkv",
            ["-map", "0", "-c", "copy"]),
        new ConversionPreset(
            "mp3-192",
            "MP3 audio (192 kbps)",
            "Commonly compatible MP3 audio at 192 kbps. Video is dropped.",
            ".mp3",
            [.. MapAudioOnly, "-c:a", "libmp3lame", "-b:a", "192k"]),
        new ConversionPreset(
            "flac-lossless",
            "FLAC audio (lossless)",
            "Lossless FLAC audio — perfect quality, larger files. Video is dropped.",
            ".flac",
            [.. MapAudioOnly, "-c:a", "flac"]),
        new ConversionPreset(
            "wav-pcm",
            "WAV audio (PCM 16-bit)",
            "Uncompressed PCM WAV audio — the studio default. Video is dropped.",
            ".wav",
            [.. MapAudioOnly, "-c:a", "pcm_s16le"]),
    ];

    public static ConversionPreset? Find(string id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));

    public static bool IsSupportedOutputExtension(string extension) =>
        All.Any(p => string.Equals(p.TargetExtension, extension, StringComparison.OrdinalIgnoreCase));
}