using System.Text;

namespace PlaySphere.Subtitles;

/// <summary>
/// Exports a parsed <see cref="SubtitleDocument"/> to SRT — the most broadly compatible
/// subtitle format — regardless of the format it was originally parsed from (PICKLE.md §17
/// "subtitle export"; §38/§41 requires AI-generated subtitles stay separate tracks until an
/// explicit export like this one).
/// </summary>
public static class SubtitleExporter
{
    /// <summary>Renders <paramref name="document"/> as SRT text.</summary>
    public static string ToSrt(SubtitleDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var sb = new StringBuilder();
        var index = 1;
        foreach (var cue in document.Cues)
        {
            sb.Append(index++).Append('\n');
            sb.Append(FormatTimestamp(cue.Start)).Append(" --> ").Append(FormatTimestamp(cue.End)).Append('\n');
            sb.Append(cue.Text).Append('\n');
            sb.Append('\n');
        }

        return sb.ToString();
    }

    /// <summary>Renders <paramref name="document"/> as SRT and writes it to <paramref name="outputPath"/>.</summary>
    public static Task WriteSrtAsync(SubtitleDocument document, string outputPath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        return File.WriteAllTextAsync(outputPath, ToSrt(document), cancellationToken);
    }

    private static string FormatTimestamp(TimeSpan ts)
    {
        if (ts < TimeSpan.Zero)
        {
            ts = TimeSpan.Zero;
        }

        return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2},{ts.Milliseconds:D3}";
    }
}
