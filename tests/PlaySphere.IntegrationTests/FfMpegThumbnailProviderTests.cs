using Microsoft.Extensions.Logging.Abstractions;
using PlaySphere.Media.Export;
using Xunit;

namespace PlaySphere.IntegrationTests;

/// <summary>
/// Tests for <see cref="FfMpegThumbnailProvider"/> against the real bundled ffmpeg.exe on a
/// synthetic media fixture (PICKLE.md §20 "hover thumbnail" / PS-704). Verifies a real contact-sheet
/// filmstrip is produced and cached, plus graceful bounded failure for untrusted/invalid inputs.
/// </summary>
public sealed class FfMpegThumbnailProviderTests : IDisposable
{
    private readonly string _directory;
    private readonly string _cache;
    private readonly FfMpegThumbnailProvider _provider;

    public FfMpegThumbnailProviderTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PlaySphereThumbnailTests_" + Guid.NewGuid().ToString("N"));
        _cache = Path.Combine(_directory, "cache");
        Directory.CreateDirectory(_directory);
        _provider = new FfMpegThumbnailProvider(
            NullLogger<FfMpegThumbnailProvider>.Instance,
            new ThumbnailStripOptions { ExecutablePath = MediaTestTools.Ffmpeg });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task GenerateStripAsync_produces_a_cacheable_filmstrip()
    {
        if (MediaTestTools.Ffmpeg is null)
        {
            return;
        }

        var input = await MediaTestTools.GenerateSampleVideoAsync(Path.Combine(_directory, "sample.mp4"));

        var result = await _provider.GenerateStripAsync(input, _cache, TimeSpan.FromSeconds(2), 16);

        Assert.True(result.Success, result.ErrorMessage);
        Assert.True(File.Exists(result.StripPath));
        Assert.True(new FileInfo(result.StripPath).Length > 0);
        Assert.Equal(4, result.Columns);
        Assert.Equal(4, result.Rows);
        Assert.True(result.CellWidth > 0);
        Assert.True(result.CellHeight > 0);

        // Cache hit: same file identity reuses the sheet without a second ffmpeg run.
        var cached = await _provider.GenerateStripAsync(input, _cache, TimeSpan.FromSeconds(2), 16);
        Assert.True(cached.Success);
        Assert.Equal(result.StripPath, cached.StripPath);
    }

    [Fact]
    public async Task GenerateStripAsync_fails_for_a_missing_input_file()
    {
        var result = await _provider.GenerateStripAsync(Path.Combine(_directory, "missing.mp4"), _cache, TimeSpan.FromSeconds(2), 16);

        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task GenerateStripAsync_fails_for_unknown_duration()
    {
        if (MediaTestTools.Ffmpeg is null)
        {
            return;
        }

        var input = await MediaTestTools.GenerateSampleVideoAsync(Path.Combine(_directory, "sample2.mp4"));

        var result = await _provider.GenerateStripAsync(input, _cache, TimeSpan.Zero, 16);

        Assert.False(result.Success);
        Assert.Contains("duration", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GenerateStripAsync_fails_softly_when_ffmpeg_is_missing()
    {
        var noToolProvider = new FfMpegThumbnailProvider(
            NullLogger<FfMpegThumbnailProvider>.Instance,
            new ThumbnailStripOptions { ExecutablePath = Path.Combine(_directory, "does-not-exist-ffmpeg.exe") });

        var input = await MediaTestTools.GenerateSampleVideoAsync(Path.Combine(_directory, "sample3.mp4"));
        var result = await noToolProvider.GenerateStripAsync(input, _cache, TimeSpan.FromSeconds(2), 16);

        Assert.False(result.Success);
        Assert.Contains("ffmpeg was not found", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}