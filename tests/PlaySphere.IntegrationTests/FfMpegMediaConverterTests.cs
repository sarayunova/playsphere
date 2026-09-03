using Microsoft.Extensions.Logging.Abstractions;
using PlaySphere.Domain.Media;
using PlaySphere.Media.Export;
using Xunit;

namespace PlaySphere.IntegrationTests;

/// <summary>
/// Tests for <see cref="FfMpegMediaConverter"/> against the real bundled ffmpeg.exe on a synthetic
/// media fixture (PICKLE.md §29 conversion / PS-703). Verifies a real transcode, a real stream-copy
/// remux, and graceful bounded failure for untrusted/invalid inputs.
/// </summary>
public sealed class FfMpegMediaConverterTests : IDisposable
{
    private readonly string _directory;
    private readonly FfMpegMediaConverter _converter;

    public FfMpegMediaConverterTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PlaySphereConvertTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _converter = new FfMpegMediaConverter(NullLogger<FfMpegMediaConverter>.Instance, new FfMpegConvertOptions { ExecutablePath = MediaTestTools.Ffmpeg });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task ConvertAsync_transcodes_an_mp4_to_mp3()
    {
        if (MediaTestTools.Ffmpeg is null)
        {
            return;
        }

        var input = await MediaTestTools.GenerateSampleVideoAsync(Path.Combine(_directory, "sample.mp4"));
        var output = Path.Combine(_directory, "song.mp3");
        var preset = ConversionPresets.Find("mp3-192")!;

        var result = await _converter.ConvertAsync(input, output, preset);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(File.Exists(output));
        Assert.True(new FileInfo(output).Length > 0);
    }

    [Fact]
    public async Task ConvertAsync_remuxes_an_mp4_to_mkv_with_stream_copy()
    {
        if (MediaTestTools.Ffmpeg is null)
        {
            return;
        }

        var input = await MediaTestTools.GenerateSampleVideoAsync(Path.Combine(_directory, "sample2.mp4"));
        var output = Path.Combine(_directory, "remux.mkv");
        var preset = ConversionPresets.Find("mkv-remux")!;

        var result = await _converter.ConvertAsync(input, output, preset);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(File.Exists(output));
    }

    [Fact]
    public async Task ConvertAsync_fails_for_a_missing_input_file()
    {
        var output = Path.Combine(_directory, "song.mp3");
        var preset = ConversionPresets.Find("mp3-192")!;

        var result = await _converter.ConvertAsync(Path.Combine(_directory, "missing.mp4"), output, preset);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task ConvertAsync_fails_when_the_output_extension_mismatches_the_preset()
    {
        var input = await MediaTestTools.GenerateSampleVideoAsync(Path.Combine(_directory, "sample3.mp4"));
        var output = Path.Combine(_directory, "song.mp4");
        var preset = ConversionPresets.Find("mp3-192")!;

        var result = await _converter.ConvertAsync(input, output, preset);

        Assert.False(result.Success);
        Assert.Contains("does not match the", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task ConvertAsync_fails_softly_when_ffmpeg_is_missing()
    {
        var noToolConverter = new FfMpegMediaConverter(
            NullLogger<FfMpegMediaConverter>.Instance,
            new FfMpegConvertOptions { ExecutablePath = Path.Combine(_directory, "does-not-exist-ffmpeg.exe") });

        var input = await MediaTestTools.GenerateSampleVideoAsync(Path.Combine(_directory, "sample4.mp4"));
        var result = await noToolConverter.ConvertAsync(input, Path.Combine(_directory, "song.mp3"), ConversionPresets.Find("mp3-192")!);

        Assert.False(result.Success);
        Assert.Contains("ffmpeg was not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}