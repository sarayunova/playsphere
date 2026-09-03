using PlaySphere.Domain.Media;
using PlaySphere.Playback.Abstractions;
using Xunit;

namespace PlaySphere.UnitTests;

/// <summary>
/// Pure tests for the subtitle style model (PICKLE.md §17 "styling") and its constraint clamps.
/// </summary>
public sealed class SubtitleStyleTests
{
    [Fact]
    public void Default_style_uses_system_defaults()
    {
        var d = SubtitleStyleOptions.Default;
        Assert.Equal(100, d.TextScalePercent);
        Assert.Null(d.FontFamily);
        Assert.Null(d.TextColorArgb);
        Assert.Null(d.OutlineColorArgb);
        Assert.Equal(SubtitleOutlineStyle.Off, d.Outline);
        Assert.Equal(0, d.VerticalMarginPx);
    }

    [Fact]
    public void With_io_preserves_unrelated_fields()
    {
        var style = SubtitleStyleOptions.Default with
        {
            FontFamily = "Segoe UI",
            TextColorArgb = 0xFFFF00,
            TextScalePercent = 150,
        };

        Assert.Equal("Segoe UI", style.FontFamily);
        Assert.Equal(0xFFFF00, style.TextColorArgb);
        Assert.Null(style.OutlineColorArgb);
        Assert.Equal(SubtitleOutlineStyle.Off, style.Outline);
        Assert.Equal(0, style.VerticalMarginPx);
    }

    [Fact]
    public void ClampSubtitleColor_bounds_to_0rgbb_range()
    {
        Assert.Equal(0xFFFFFF, PlaybackConstraints.ClampSubtitleColor(0xFFFFFF));
        Assert.Equal(0xFFFFFF, PlaybackConstraints.ClampSubtitleColor(0x1234567));
        Assert.Equal(0x000000, PlaybackConstraints.ClampSubtitleColor(-5));
        Assert.Equal(0x00FF00, PlaybackConstraints.ClampSubtitleColor(0x00FF00));
    }

    [Fact]
    public void ClampVerticalMarginPx_bounds_to_range()
    {
        Assert.Equal(0, PlaybackConstraints.ClampVerticalMarginPx(-10));
        Assert.Equal(500, PlaybackConstraints.ClampVerticalMarginPx(500));
        Assert.Equal(1000, PlaybackConstraints.ClampVerticalMarginPx(5000));
    }

    [Theory]
    [InlineData(SubtitleOutlineStyle.Off, 0)]
    [InlineData(SubtitleOutlineStyle.Thin, 1)]
    [InlineData(SubtitleOutlineStyle.Normal, 2)]
    public void Outline_style_maps_to_freetype_thickness(SubtitleOutlineStyle value, int expected)
    {
        Assert.Equal(expected, (int)value);
    }
}
