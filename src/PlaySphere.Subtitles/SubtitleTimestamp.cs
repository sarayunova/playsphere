using System.Globalization;
using System.Text.RegularExpressions;

namespace PlaySphere.Subtitles;

/// <summary>
/// Parses the timestamp forms shared by SRT (<c>HH:MM:SS,mmm</c>) and WebVTT
/// (<c>HH:MM:SS.mmm</c> or <c>MM:SS.mmm</c>). Accepts either separator so a parser can be
/// lenient about minor malformation rather than rejecting an otherwise-usable cue.
/// </summary>
internal static partial class SubtitleTimestamp
{
    [GeneratedRegex(@"^(?:(\d+):)?(\d{1,2}):(\d{2})[.,](\d{1,3})$")]
    private static partial Regex TimestampPattern();

    public static bool TryParse(string text, out TimeSpan value)
    {
        value = TimeSpan.Zero;
        var match = TimestampPattern().Match(text.Trim());
        if (!match.Success)
        {
            return false;
        }

        var hours = match.Groups[1].Success ? int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
        var minutes = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        var millisText = match.Groups[4].Value.PadRight(3, '0')[..3];
        var millis = int.Parse(millisText, CultureInfo.InvariantCulture);

        if (minutes > 59 || seconds > 59)
        {
            return false;
        }

        value = new TimeSpan(0, hours, minutes, seconds, millis);
        return true;
    }
}
