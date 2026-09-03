using Microsoft.Extensions.Logging.Abstractions;
using PlaySphere.Media.Export;
using Xunit;

namespace PlaySphere.IntegrationTests;

/// <summary>
/// Tests for <see cref="FfMpegWaveformProvider"/> against the real bundled ffmpeg.exe on synthetic
/// media fixtures (PICKLE.md §20 "waveform" / PS-705). Verifies a real peak-level PNG strip is
/// produced and cached, plus graceful bounded failure for untrusted/invalid inputs.
/// </summary>
public sealed class FfMpegWaveformProviderTests : IDisposable
{
    private readonly string _directory;
    private readonly string _cache;
    private readonly FfMpegWaveformProvider _provider;

    public FfMpegWaveformProviderTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PlaySphereWaveformTests_" + Guid.NewGuid().ToString("N"));
        _cache = Path.Combine(_directory, "cache");
        Directory.CreateDirectory(_directory);
        _provider = new FfMpegWaveformProvider(
            NullLogger<FfMpegWaveformProvider>.Instance,
            new WaveformOptions { ExecutablePath = MediaTestTools.Ffmpeg });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateAsync_produces_a_cacheable_waveform_for_video_with_audio()
    {
        if (MediaTestTools.Ffmpeg is null)
        {
            return;
        }

        var input = await MediaTestTools.GenerateSampleVideoAsync(Path.Combine(_directory, "sample.mp4"));

        var result = await _provider.GenerateAsync(input, _cache);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(File.Exists(result.ImagePath));
        Assert.True(new FileInfo(result.ImagePath).Length > 0);
        Assert.Equal(1080, result.Width);
        Assert.Equal(56, result.Height);

        // Cache hit: same file identity reuses the strip without a second ffmpeg run.
        var cached = await _provider.GenerateAsync(input, _cache);
        Assert.True(cached.Success);
        Assert.Equal(result.ImagePath, cached.ImagePath);
    }

    [Fact]
    public async Task GenerateAsync_produces_a_waveform_for_audio_only_media()
    {
        if (MediaTestTools.Ffmpeg is null)
        {
            return;
        }

        var input = await MediaTestTools.GenerateSampleAudioAsync(Path.Combine(_directory, "audio.wav"));

        var result = await _provider.GenerateAsync(input, _cache);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(File.Exists(result.ImagePath));
    }

    [Fact]
    public async Task GenerateAsync_fails_softly_when_the_file_has_no_audio_track()
    {
        if (MediaTestTools.Ffmpeg is null)
        {
            return;
        }

        var input = await MediaTestTools.GenerateSampleVideoNoAudioAsync(Path.Combine(_directory, "noaudio.mp4"));

        var result = await _provider.GenerateAsync(input, _cache);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task GenerateAsync_fails_for_a_missing_input_file()
    {
        var result = await _provider.GenerateAsync(Path.Combine(_directory, "missing.mp4"), _cache);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task GenerateAsync_fails_softly_when_ffmpeg_is_missing()
    {
        var noToolProvider = new FfMpegWaveformProvider(
            NullLogger<FfMpegWaveformProvider>.Instance,
            new WaveformOptions { ExecutablePath = Path.Combine(_directory, "does-not-exist-ffmpeg.exe") });

        var input = await MediaTestTools.GenerateSampleAudioAsync(Path.Combine(_directory, "audio2.wav"));
        var result = await noToolProvider.GenerateAsync(input, _cache);

        Assert.False(result.Success);
        Assert.Contains("ffmpeg was not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}