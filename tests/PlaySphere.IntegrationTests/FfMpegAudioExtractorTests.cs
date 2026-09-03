using Microsoft.Extensions.Logging.Abstractions;
using PlaySphere.Media.Export;
using PlaySphere.Domain.Media;
using Xunit;

namespace PlaySphere.IntegrationTests;

/// <summary>
/// Tests for <see cref="FfMpegAudioExtractor"/> against the real bundled ffmpeg.exe on synthetic
/// media fixtures (PICKLE.md §38 audio extraction, AI-02). Verifies a real video→WAV extraction that
/// feeds AI transcription, plus graceful bounded failure for untrusted/invalid inputs.
/// </summary>
public sealed class FfMpegAudioExtractorTests : IDisposable
{
    private readonly string _directory;
    private readonly FfMpegAudioExtractor _extractor;

    public FfMpegAudioExtractorTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PlaySphereAudioExtractTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _extractor = new FfMpegAudioExtractor(NullLogger<FfMpegAudioExtractor>.Instance, new FfMpegAudioExtractOptions { ExecutablePath = MediaTestTools.Ffmpeg });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExtractAudioAsync_extracts_a_mono_wav_from_a_video()
    {
        if (MediaTestTools.Ffmpeg is null)
        {
            return;
        }

        var input = await MediaTestTools.GenerateSampleVideoAsync(Path.Combine(_directory, "sample.mp4"));
        var output = Path.Combine(_directory, "audio.wav");

        var result = await _extractor.ExtractAudioAsync(input, output);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(output, result.OutputPath);
        Assert.True(File.Exists(output));
        Assert.True(new FileInfo(output).Length > 0);

        // A mono 16 kHz 16-bit WAV is 44 bytes header + 2 bytes/sample * 16000 * seconds.
        // The ~2s fixture must produce a real (small but non-empty) PCM body.
        Assert.True(new FileInfo(output).Length > 1000, $"expected a real PCM body, got {new FileInfo(output).Length} bytes");
    }

    [Fact]
    public async Task ExtractAudioAsync_fails_when_the_input_has_no_audio_stream()
    {
        if (MediaTestTools.Ffmpeg is null)
        {
            return;
        }

        var input = await MediaTestTools.GenerateSampleVideoNoAudioAsync(Path.Combine(_directory, "silent.mp4"));
        var output = Path.Combine(_directory, "silent.wav");

        var result = await _extractor.ExtractAudioAsync(input, output);

        Assert.False(result.Success);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task ExtractAudioAsync_fails_for_a_missing_input_file()
    {
        var output = Path.Combine(_directory, "audio.wav");

        var result = await _extractor.ExtractAudioAsync(Path.Combine(_directory, "missing.mp4"), output);

        Assert.False(result.Success);
        Assert.Contains("does not exist", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task ExtractAudioAsync_fails_unless_the_output_is_a_wav()
    {
        var input = await MediaTestTools.GenerateSampleVideoAsync(Path.Combine(_directory, "sample2.mp4"));
        var output = Path.Combine(_directory, "audio.mp3");

        var result = await _extractor.ExtractAudioAsync(input, output);

        Assert.False(result.Success);
        Assert.Contains(".wav", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task ExtractAudioAsync_fails_softly_when_ffmpeg_is_missing()
    {
        var noToolExtractor = new FfMpegAudioExtractor(
            NullLogger<FfMpegAudioExtractor>.Instance,
            new FfMpegAudioExtractOptions { ExecutablePath = Path.Combine(_directory, "does-not-exist-ffmpeg.exe") });

        var input = await MediaTestTools.GenerateSampleVideoAsync(Path.Combine(_directory, "sample3.mp4"));
        var result = await noToolExtractor.ExtractAudioAsync(input, Path.Combine(_directory, "audio.wav"));

        Assert.False(result.Success);
        Assert.Contains("ffmpeg was not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
