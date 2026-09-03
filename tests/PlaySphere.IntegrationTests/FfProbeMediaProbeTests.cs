using Microsoft.Extensions.Logging.Abstractions;
using PlaySphere.Media.Probe;
using Xunit;

namespace PlaySphere.IntegrationTests;

/// <summary>
/// Tests for <see cref="FfProbeMediaProbe"/> against the real bundled ffprobe.exe on a synthetic
/// media fixture (PICKLE.md §23). Verifies both successful metadata extraction and graceful,
/// bounded failure for untrusted/non-probeable inputs.
/// </summary>
public sealed class FfProbeMediaProbeTests : IDisposable
{
    private readonly string _directory;
    private readonly FfProbeMediaProbe _probe;

    public FfProbeMediaProbeTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PlaySphereProbeTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _probe = new FfProbeMediaProbe(NullLogger<FfProbeMediaProbe>.Instance, new FfProbeOptions { ExecutablePath = MediaTestTools.FfProbe });
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task ProbeAsync_returns_technical_metadata_for_a_real_media_file()
    {
        if (MediaTestTools.FfProbe is null)
        {
            return;
        }
        var file = await MediaTestTools.GenerateSampleVideoAsync(Path.Combine(_directory, "sample.mp4"));

        var result = await _probe.ProbeAsync(file);

        Assert.NotNull(result);
        Assert.InRange(result.Duration.TotalSeconds, 1.5, 2.5);
        Assert.Equal("320x180", result.Resolution);
        Assert.Equal("h264", result.Codec);
        Assert.NotNull(result.FrameRate);
        Assert.InRange(result.FrameRate!.Value, 4.5, 5.5);
        Assert.True(result.BitrateKbps > 0);
        Assert.Equal(1, result.AudioTrackCount);
        Assert.Equal(0, result.SubtitleTrackCount);
        Assert.Equal(320, result.Width);
        Assert.Equal(180, result.Height);
    }

    [Fact]
    public async Task ProbeAsync_returns_null_for_a_non_media_file()
    {
        var notMedia = Path.Combine(_directory, "notes.txt");
        File.WriteAllText(notMedia, "not media");

        var result = await _probe.ProbeAsync(notMedia);

        Assert.Null(result);
    }

    [Fact]
    public async Task ProbeAsync_returns_null_for_a_missing_file()
    {
        var missing = Path.Combine(_directory, "missing.mp4");

        var result = await _probe.ProbeAsync(missing);

        Assert.Null(result);
    }

    [Fact]
    public async Task ProbeAsync_returns_null_when_no_executable_is_available()
    {
        var noToolProbe = new FfProbeMediaProbe(
            NullLogger<FfProbeMediaProbe>.Instance,
            new FfProbeOptions { ExecutablePath = Path.Combine(_directory, "does-not-exist-ffprobe.exe") });

        var result = await noToolProbe.ProbeAsync(Path.Combine(_directory, "anything.mp4"));

        Assert.Null(result);
    }
}
