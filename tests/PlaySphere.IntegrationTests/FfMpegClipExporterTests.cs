using Microsoft.Extensions.Logging.Abstractions;
using PlaySphere.Media.Export;
using Xunit;

namespace PlaySphere.IntegrationTests;

/// <summary>
/// Tests for <see cref="FfMpegClipExporter"/> against the real bundled ffmpeg.exe on a synthetic
/// media fixture (PICKLE.md §1102 clip export). Verifies both a real stream-copy trim and graceful,
/// bounded failure for untrusted/invalid inputs.
/// </summary>
public sealed class FfMpegClipExporterTests : IDisposable
{
    private readonly string _directory;
    private readonly FfMpegClipExporter _exporter;

    public FfMpegClipExporterTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PlaySphereClipExportTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _exporter = new FfMpegClipExporter(NullLogger<FfMpegClipExporter>.Instance, new FfMpegExportOptions { ExecutablePath = MediaTestTools.Ffmpeg });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task ExportClipAsync_trims_a_real_media_file()
    {
        if (MediaTestTools.Ffmpeg is null)
        {
            return;
        }

        var input = await MediaTestTools.GenerateSampleVideoAsync(Path.Combine(_directory, "sample.mp4"));
        var output = Path.Combine(_directory, "clip.mp4");

        // 2s source; export the 0.5s→1.5s window (1.0s in duration).
        var start = TimeSpan.FromMilliseconds(500);
        var duration = TimeSpan.FromSeconds(1);

        var result = await _exporter.ExportClipAsync(input, output, start, duration);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(File.Exists(output));
        Assert.True(new FileInfo(output).Length > 0);
    }

    [Fact]
    public async Task ExportClipAsync_fails_for_a_missing_input_file()
    {
        var output = Path.Combine(_directory, "clip.mp4");

        var result = await _exporter.ExportClipAsync(Path.Combine(_directory, "missing.mp4"), output, TimeSpan.Zero, TimeSpan.FromSeconds(1));

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task ExportClipAsync_fails_for_an_unsupported_output_extension()
    {
        var input = await MediaTestTools.GenerateSampleVideoAsync(Path.Combine(_directory, "sample2.mp4"));
        var output = Path.Combine(_directory, "clip.exe");

        var result = await _exporter.ExportClipAsync(input, output, TimeSpan.Zero, TimeSpan.FromSeconds(1));

        Assert.False(result.Success);
        Assert.Contains("container", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExportClipAsync_fails_for_invalid_bounds()
    {
        var input = await MediaTestTools.GenerateSampleVideoAsync(Path.Combine(_directory, "sample3.mp4"));
        var output = Path.Combine(_directory, "clip.mp4");

        var negative = await _exporter.ExportClipAsync(input, output, TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(1));
        Assert.False(negative.Success);

        var zeroDuration = await _exporter.ExportClipAsync(input, output, TimeSpan.Zero, TimeSpan.Zero);
        Assert.False(zeroDuration.Success);
    }

    [Fact]
    public async Task ExportClipAsync_fails_softly_when_ffmpeg_is_missing()
    {
        var noToolExporter = new FfMpegClipExporter(
            NullLogger<FfMpegClipExporter>.Instance,
            new FfMpegExportOptions { ExecutablePath = Path.Combine(_directory, "does-not-exist-ffmpeg.exe") });

        var input = await MediaTestTools.GenerateSampleVideoAsync(Path.Combine(_directory, "sample4.mp4"));
        var result = await noToolExporter.ExportClipAsync(input, Path.Combine(_directory, "clip.mp4"), TimeSpan.Zero, TimeSpan.FromSeconds(1));

        Assert.False(result.Success);
        Assert.Contains("ffmpeg was not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}