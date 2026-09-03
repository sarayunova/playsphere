namespace PlaySphere.Subtitles;

/// <summary>Resolves the appropriate <see cref="ISubtitleParser"/> for a subtitle file extension.</summary>
public static class SubtitleParserFactory
{
    /// <summary>Detects the subtitle format from a file path's extension.</summary>
    public static SubtitleFormat DetectFormat(string filePath) =>
        Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".srt" => SubtitleFormat.Srt,
            ".vtt" => SubtitleFormat.WebVtt,
            _ => SubtitleFormat.Unknown,
        };

    /// <summary>
    /// Returns a parser for <paramref name="format"/>, or <see langword="null"/> when the format
    /// has no parser yet (e.g. <see cref="SubtitleFormat.Unknown"/>; ASS/SSA/SUB are rendered
    /// by the playback engine directly and are not parsed by this module).
    /// </summary>
    public static ISubtitleParser? ForFormat(SubtitleFormat format) => format switch
    {
        SubtitleFormat.Srt => new SrtSubtitleParser(),
        SubtitleFormat.WebVtt => new WebVttSubtitleParser(),
        _ => null,
    };

    /// <summary>Convenience: detects format from <paramref name="filePath"/>'s extension and resolves a parser for it.</summary>
    public static ISubtitleParser? ForFile(string filePath) => ForFormat(DetectFormat(filePath));
}
