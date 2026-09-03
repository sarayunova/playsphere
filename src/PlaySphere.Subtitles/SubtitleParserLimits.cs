namespace PlaySphere.Subtitles;

/// <summary>
/// Defensive bounds applied while parsing untrusted subtitle input (PICKLE.md §43: parser limits).
/// Parsers stop accepting further cues once <see cref="MaxCueCount"/> is reached rather than
/// throwing, so a malicious or corrupted file degrades to a truncated result instead of an
/// unbounded allocation or crash.
/// </summary>
public static class SubtitleParserLimits
{
    /// <summary>Subtitle files larger than this are rejected by <see cref="ISubtitleParser"/> implementations.</summary>
    public const int MaxContentLengthChars = 8 * 1024 * 1024;

    /// <summary>Maximum cues retained from a single subtitle document.</summary>
    public const int MaxCueCount = 50_000;

    /// <summary>Maximum characters retained per cue's text (excess is truncated).</summary>
    public const int MaxCueTextLength = 4_000;
}
