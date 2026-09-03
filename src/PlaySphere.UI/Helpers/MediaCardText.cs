using System.Globalization;
using PlaySphere.Domain.Library;

namespace PlaySphere.UI.Helpers;

/// <summary>
/// Pure, testable formatting helpers that turn a cataloged <see cref="LibraryMediaItem"/>'s
/// technical metadata (populated by FFprobe during scanning) into short display strings for the
/// library/home cards. Kept free of any UI control types so it can be unit-tested.
/// </summary>
public static class MediaCardText
{
    /// <summary>Formats a duration as m:ss or h:mm:ss; returns "Unknown" for a non-positive value.</summary>
    public static string FormatDuration(TimeSpan? duration)
    {
        if (duration is null || duration <= TimeSpan.Zero || duration.Value.TotalSeconds < 1)
        {
            return "Unknown";
        }

        var d = duration.Value;
        return d.TotalHours >= 1
            ? $"{(int)d.TotalHours}:{d.Minutes:D2}:{d.Seconds:D2}"
            : $"{d.Minutes}:{d.Seconds:D2}";
    }

    /// <summary>
    /// Concatenates the non-empty technical fields of a library item into a single muted line,
    /// e.g. "1920×1080 · H.264 · 1:23:45 · 2345 Kbps". Pieces that aren't populated are skipped.
    /// </summary>
    public static string FormatTechnicalLine(LibraryMediaItem item)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(item.Resolution))
        {
            parts.Add(item.Resolution);
        }

        if (!string.IsNullOrWhiteSpace(item.Codec))
        {
            parts.Add(item.Codec);
        }

        if (item.Duration > TimeSpan.Zero && item.Duration.TotalSeconds >= 1)
        {
            parts.Add(FormatDuration(item.Duration));
        }

        if (item.BitrateKbps is > 0)
        {
            parts.Add($"{item.BitrateKbps.Value.ToString(CultureInfo.InvariantCulture)} Kbps");
        }

        if (item.AudioTrackCount > 0)
        {
            parts.Add(item.AudioTrackCount == 1 ? "1 audio" : $"{item.AudioTrackCount} audio");
        }

        if (item.SubtitleTrackCount > 0)
        {
            parts.Add(item.SubtitleTrackCount == 1 ? "1 sub" : $"{item.SubtitleTrackCount} subs");
        }

        return parts.Count == 0 ? "No metadata" : string.Join(" · ", parts);
    }
}
