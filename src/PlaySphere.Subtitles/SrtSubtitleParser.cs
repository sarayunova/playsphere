namespace PlaySphere.Subtitles;

/// <summary>
/// Defensive SubRip (.srt) parser. Blocks are separated by a blank line; a block is
/// <c>[index]</c>, an arrow timing line, then one or more text lines. The file's own index
/// numbers are not preserved — cues are renumbered sequentially as they are accepted, so a
/// skipped malformed block does not leave a gap.
/// </summary>
public sealed class SrtSubtitleParser : ISubtitleParser
{
    public SubtitleFormat Format => SubtitleFormat.Srt;

    public SubtitleDocument Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > SubtitleParserLimits.MaxContentLengthChars)
        {
            throw new ArgumentException(
                $"Subtitle content exceeds the {SubtitleParserLimits.MaxContentLengthChars}-character limit.",
                nameof(content));
        }

        var normalized = content.Replace("\r\n", "\n").Replace('\r', '\n');
        var cues = CueBlockParser.Parse(normalized);
        return new SubtitleDocument(SubtitleFormat.Srt, cues);
    }
}
