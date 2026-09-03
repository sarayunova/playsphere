using PlaySphere.Domain.Media;
using PlaySphere.Playback.Abstractions;
using PlaySphere.Playback.LibVLC;
using Xunit;

namespace PlaySphere.PlaybackTests;

/// <summary>
/// PS-106 (Rate), PS-107 (Volume/Mute), PS-108 (Track Selection) integration tests.
/// </summary>
[Collection(LibVlcCollection.Name)]
public sealed class PlaybackControlTests
{
    private readonly LibVlcFixture _fixture;

    public PlaybackControlTests(LibVlcFixture fixture) => _fixture = fixture;

    private LibVlcPlaybackEngine CreateEngine() => _fixture.CreateEngine();

    // ── PS-106: Rate Control ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public async Task SetRateAsync_updates_Rate_property(double rate)
    {
        using var engine = CreateEngine();

        await engine.SetRateAsync(rate);

        Assert.Equal(rate, engine.Rate, precision: 2);
    }

    [Fact]
    public async Task SetRateAsync_clamps_sub_minimum_to_MinRate()
    {
        using var engine = CreateEngine();

        await engine.SetRateAsync(0.01); // Below MinRate (0.25)

        Assert.Equal(PlaybackConstraints.MinRate, engine.Rate, precision: 2);
    }

    [Fact]
    public async Task SetRateAsync_clamps_super_maximum_to_MaxRate()
    {
        using var engine = CreateEngine();

        await engine.SetRateAsync(10.0); // Above MaxRate (4.0)

        Assert.Equal(PlaybackConstraints.MaxRate, engine.Rate, precision: 2);
    }

    // ── PS-107: Volume & Mute ────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task SetVolumeAsync_updates_Volume_property(int volume)
    {
        using var engine = CreateEngine();

        await engine.SetVolumeAsync(volume);

        Assert.Equal(volume, engine.Volume);
    }

    [Fact]
    public async Task SetVolumeAsync_clamps_negative_to_zero()
    {
        using var engine = CreateEngine();

        await engine.SetVolumeAsync(-20);

        Assert.Equal(0, engine.Volume);
    }

    [Fact]
    public async Task SetVolumeAsync_clamps_over_100_to_100()
    {
        using var engine = CreateEngine();

        await engine.SetVolumeAsync(200);

        Assert.Equal(100, engine.Volume);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SetMutedAsync_toggles_IsMuted(bool muted)
    {
        using var engine = CreateEngine();

        await engine.SetMutedAsync(muted);

        Assert.Equal(muted, engine.IsMuted);
    }

    // ── PS-108: Track Selection ──────────────────────────────────────────────

    [Fact]
    public async Task SelectAudioTrackAsync_does_not_throw()
    {
        using var engine = CreateEngine();
        var media = MediaFile.FromPath(SampleFiles.Resolve("multi-audio.mkv"));
        await engine.LoadAsync(media);

        // Select first available track, then null (disable).
        int? firstTrackId = engine.Metadata.AudioTracks.Count > 0
            ? engine.Metadata.AudioTracks[0].Id
            : null;

        await engine.SelectAudioTrackAsync(firstTrackId);
        await engine.SelectAudioTrackAsync(null);
    }

    [Fact]
    public async Task SelectSubtitleTrackAsync_does_not_throw()
    {
        using var engine = CreateEngine();
        var media = MediaFile.FromPath(SampleFiles.Resolve("multi-subtitle.mkv"));
        await engine.LoadAsync(media);

        int? firstSubId = engine.Metadata.SubtitleTracks.Count > 0
            ? engine.Metadata.SubtitleTracks[0].Id
            : null;

        await engine.SelectSubtitleTrackAsync(firstSubId);
        await engine.SelectSubtitleTrackAsync(null);
    }
}
