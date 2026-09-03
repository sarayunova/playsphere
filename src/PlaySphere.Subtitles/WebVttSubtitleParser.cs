namespace PlaySphere.Subtitles;

/// <summary>
/// Defensive WebVTT (.vtt) parser. Skips the leading <c>WEBVTT</c> header block and any
/// <c>NOTE</c>/<c>STYLE</c>/<c>REGION</c> blocks; remaining blocks follow the same
/// "optional cue id, arrow timing line, text lines" shape as SRT.
/// </summary>
public sealed class WebVttSubtitleParser : ISubtitleParser
{
    private static readonly string[] NonCueKeywords = ["WEBVTT", "NOTE", "STYLE", "REGION"];

    public SubtitleFormat Format => SubtitleFormat.WebVtt;

    public SubtitleDocument Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (content.Length > SubtitleParserLimits.MaxContentLengthChars)
        {
            throw new ArgumentException(
                $"Subtitle content exceeds the {SubtitleParserLimits.MaxContentLengthChars}-character limit.",
                nameof(content));
        }

        var normalized = content.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n');
        var cues = CueBlockParser.Parse(
            normalized,
            block => NonCueKeywords.Any(keyword => block.TrimStart().StartsWith(keyword, StringComparison.Ordinal)));
        return new SubtitleDocument(SubtitleFormat.WebVtt, cues);
    }
}
