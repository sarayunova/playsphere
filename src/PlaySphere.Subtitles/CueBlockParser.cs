namespace PlaySphere.Subtitles;

/// <summary>
/// Shared block-based parsing loop for the SRT/WebVTT "optional id, arrow timing line, text
/// lines, blank line" shape. A block failing to yield a valid arrow timing line, or whose
/// resulting cue has end &lt;= start, is skipped defensively rather than aborting the parse.
/// </summary>
internal static class CueBlockParser
{
    public static List<SubtitleCue> Parse(string normalizedContent, Func<string, bool>? skipBlock = null)
    {
        var blocks = normalizedContent.Split("\n\n", StringSplitOptions.RemoveEmptyEntries);
        var cues = new List<SubtitleCue>();

        foreach (var block in blocks)
        {
            if (cues.Count >= SubtitleParserLimits.MaxCueCount)
            {
                break;
            }

            if (skipBlock?.Invoke(block) == true)
            {
                continue;
            }

            var lines = block.Split('\n');
            var arrowLineIndex = Array.FindIndex(lines, l => l.Contains("-->", StringComparison.Ordinal));
            if (arrowLineIndex < 0)
            {
                continue;
            }

            var arrowParts = lines[arrowLineIndex].Split("-->", 2);
            if (!SubtitleTimestamp.TryParse(arrowParts[0], out var start) ||
                !SubtitleTimestamp.TryParse(FirstToken(arrowParts[1]), out var end))
            {
                continue;
            }

            var text = string.Join('\n', lines[(arrowLineIndex + 1)..]).Trim();
            if (text.Length > SubtitleParserLimits.MaxCueTextLength)
            {
                text = text[..SubtitleParserLimits.MaxCueTextLength];
            }

            var cue = new SubtitleCue(cues.Count + 1, start, end, text);
            if (cue.IsValid)
            {
                cues.Add(cue);
            }
        }

        return cues;
    }

    private static string FirstToken(string text) =>
        text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries) is [var first, ..] ? first : text.Trim();
}
