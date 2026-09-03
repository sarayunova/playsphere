using PlaySphere.Domain.Media;
using Xunit;

namespace PlaySphere.UnitTests;

/// <summary>
/// Pure catalog tests for <see cref="ConversionPresets"/> — every preset's ffmpeg argument slice is
/// deliberately constructed here so a malformed preset (wrong container, no video map on a video
/// preset, missing '-c copy' on the remux preset, etc.) fails the build-fast unit layer before any
/// real ffmpeg run.
/// </summary>
public sealed class ConversionPresetTests
{
    [Fact]
    public void All_exposes_a_bounded_catalog_covering_each_conversion_kind()
    {
        Assert.True(ConversionPresets.All.Count >= 6);

        var ids = ConversionPresets.All.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Contains("mp4-h264", ids);
        Assert.Contains("mp4-h265", ids);
        Assert.Contains("mkv-h264", ids);
        Assert.Contains("mkv-h265", ids);
        Assert.Contains("mkv-remux", ids);     // remux (PICKLE.md §29)
        Assert.Contains("mp3-192", ids);       // video-to-audio
        Assert.Contains("flac-lossless", ids); // audio conversion
        Assert.Contains("wav-pcm", ids);       // audio conversion
    }

    [Fact]
    public void Preset_ids_are_unique()
    {
        var duplicates = ConversionPresets.All
            .GroupBy(p => p.Id, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact]
    public void Target_extensions_are_normalized_dot_lowercase()
    {
        foreach (var preset in ConversionPresets.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(preset.TargetExtension));
            Assert.StartsWith(".", preset.TargetExtension);
            Assert.Equal(preset.TargetExtension, preset.TargetExtension.ToLowerInvariant());
            Assert.NotNull(preset.DisplayName);
            Assert.NotNull(preset.Description);
            Assert.NotEmpty(preset.FfmpegArgs);
            Assert.InRange(preset.TargetExtension.Length, 4, 5); // ".mp4" … ".wav"
        }
    }

    [Theory]
    [InlineData("mp4-h264")]
    [InlineData("mp4-h265")]
    [InlineData("mkv-h264")]
    [InlineData("mkv-h265")]
    public void Video_presets_map_first_video_and_audio_conditionally(string id)
    {
        var preset = Assert.IsType<ConversionPreset>(ConversionPresets.Find(id));
        var args = preset.FfmpegArgs;

        Assert.Contains("0:v:0?", args);
        Assert.Contains("0:a:0?", args);
        Assert.Contains("-c:v", args);
        Assert.Contains("-c:a", args);
        Assert.DoesNotContain("-vn", args);
    }

    [Theory]
    [InlineData("mp3-192")]
    [InlineData("flac-lossless")]
    [InlineData("wav-pcm")]
    public void Audio_only_presets_drop_video(string id)
    {
        var preset = Assert.IsType<ConversionPreset>(ConversionPresets.Find(id));
        var args = preset.FfmpegArgs;

        Assert.Contains("-vn", args);
        Assert.Contains("0:a:0?", args);
        Assert.DoesNotContain("0:v:0?", args);
        Assert.DoesNotContain("-c:v", args);
    }

    [Fact]
    public void Remux_preset_stream_copies_everything()
    {
        var preset = Assert.IsType<ConversionPreset>(ConversionPresets.Find("mkv-remux"));
        var args = preset.FfmpegArgs;

        Assert.Contains("-map", args);
        Assert.Contains("0", args);
        Assert.Contains("-c", args);
        Assert.Contains("copy", args);
        Assert.DoesNotContain("-crf", args);
    }

    [Fact]
    public void Find_returns_null_for_unknown_id()
    {
        Assert.Null(ConversionPresets.Find("does-not-exist"));
    }

    [Fact]
    public void IsSupportedOutputExtension_matches_catalog_containers()
    {
        Assert.True(ConversionPresets.IsSupportedOutputExtension(".mp4"));
        Assert.True(ConversionPresets.IsSupportedOutputExtension(".MP3"));
        Assert.True(ConversionPresets.IsSupportedOutputExtension(".wav"));
        Assert.False(ConversionPresets.IsSupportedOutputExtension(".exe"));
        Assert.False(ConversionPresets.IsSupportedOutputExtension("mp4"));
    }
}