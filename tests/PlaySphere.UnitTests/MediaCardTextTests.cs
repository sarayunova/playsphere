using PlaySphere.Domain.Library;
using PlaySphere.UI.Helpers;
using Xunit;

namespace PlaySphere.UnitTests;

public sealed class MediaCardTextTests
{
    [Theory]
    [InlineData(null, "Unknown")]
    [InlineData(0d, "Unknown")]
    [InlineData(-5d, "Unknown")]
    public void FormatDuration_handles_missing_or_non_positive(double? seconds, string expected)
    {
        var ts = seconds is null ? (TimeSpan?)null : TimeSpan.FromSeconds(seconds.Value);
        Assert.Equal(expected, MediaCardText.FormatDuration(ts));
    }

    [Theory]
    [InlineData(125, "2:05")]
    [InlineData(65, "1:05")]
    [InlineData(30, "0:30")]
    public void FormatDuration_formats_minutes_seconds(double seconds, string expected)
    {
        Assert.Equal(expected, MediaCardText.FormatDuration(TimeSpan.FromSeconds(seconds)));
    }

    [Theory]
    [InlineData(3600, "1:00:00")]
    [InlineData(5025, "1:23:45")]
    [InlineData(7350, "2:02:30")]
    public void FormatDuration_formats_hours(double seconds, string expected)
    {
        Assert.Equal(expected, MediaCardText.FormatDuration(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void FormatTechnicalLine_returns_no_metadata_when_no_fields_populated()
    {
        var item = new LibraryMediaItem { FilePath = "x.mp4", Fingerprint = "h" };
        Assert.Equal("No metadata", MediaCardText.FormatTechnicalLine(item));
    }

    [Fact]
    public void FormatTechnicalLine_joins_populated_fields_in_order()
    {
        var item = new LibraryMediaItem
        {
            FilePath = "x.mp4",
            Fingerprint = "h",
            Resolution = "1920x1080",
            Codec = "H.264",
            Duration = TimeSpan.FromHours(1) + TimeSpan.FromMinutes(23) + TimeSpan.FromSeconds(45),
            BitrateKbps = 2345,
            AudioTrackCount = 2,
            SubtitleTrackCount = 1,
        };

        Assert.Equal(
            "1920x1080 · H.264 · 1:23:45 · 2345 Kbps · 2 audio · 1 sub",
            MediaCardText.FormatTechnicalLine(item));
    }

    [Fact]
    public void FormatTechnicalLine_skips_missing_fields()
    {
        var item = new LibraryMediaItem
        {
            FilePath = "x.mp4",
            Fingerprint = "h",
            Codec = "AAC",
            SubtitleTrackCount = 3,
        };

        Assert.Equal("AAC · 3 subs", MediaCardText.FormatTechnicalLine(item));
    }
}
