namespace PlaySphere.Domain.Media;

/// <summary>
/// A single, user-visible media-conversion target (PICKLE.md §29). The preset is pure data:
/// <see cref="FfmpegArgs"/> is the ordered, structured argument list appended after the ffmpeg
/// input (codecs + stream maps). Ffmpeg argument strings here are data, exactly like the libVLC
/// option strings in <see cref="SubtitleStyleOptions"/> — they are never concatenated into a shell
/// command string (AGENTS.md rule 4 / PICKLE.md §43).
/// </summary>
public sealed record ConversionPreset(
    string Id,
    string DisplayName,
    string Description,
    string TargetExtension,
    IReadOnlyList<string> FfmpegArgs);