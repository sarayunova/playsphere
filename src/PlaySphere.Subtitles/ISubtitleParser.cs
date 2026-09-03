namespace PlaySphere.Subtitles;

/// <summary>
/// Parses subtitle file content into a <see cref="SubtitleDocument"/>. Implementations treat
/// input as untrusted (PICKLE.md §43): malformed blocks are skipped rather than throwing, and
/// output is bounded by <see cref="SubtitleParserLimits"/>.
/// </summary>
public interface ISubtitleParser
{
    /// <summary>Format this parser produces.</summary>
    SubtitleFormat Format { get; }

    /// <summary>
    /// Parses <paramref name="content"/> into a cue list. Never throws on malformed cues —
    /// they are skipped. Throws <see cref="ArgumentException"/> only when <paramref name="content"/>
    /// exceeds <see cref="SubtitleParserLimits.MaxContentLengthChars"/>.
    /// </summary>
    SubtitleDocument Parse(string content);
}
