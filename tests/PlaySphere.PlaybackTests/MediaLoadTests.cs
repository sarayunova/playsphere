using Microsoft.Extensions.Logging.Abstractions;
using PlaySphere.Domain.Media;
using PlaySphere.Playback.Abstractions;
using PlaySphere.Playback.LibVLC;
using Xunit;

namespace PlaySphere.PlaybackTests;

/// <summary>
/// PS-103: media load against the real libVLC adapter. Covers metadata/track/duration
/// extraction, the EMPTY→LOADING→READY transition, and error recovery (a failed load must
/// not prevent a subsequent successful load).
/// </summary>
[Collection(LibVlcCollection.Name)]
public sealed class MediaLoadTests
{
    private readonly LibVlcFixture _fixture;
    private readonly List<PlaybackState> _transitions = [];

    public MediaLoadTests(LibVlcFixture fixture) => _fixture = fixture;

    private LibVlcPlaybackEngine CreateEngine()
    {
        var engine = _fixture.CreateEngine();
        engine.StateChanged += (_, e) => _transitions.Add(e.Current);
        return engine;
    }

    [Fact]
    public async Task Load_mp3_enters_Ready_with_audio_track_and_duration()
    {
        using var engine = CreateEngine();
        var media = MediaFile.FromPath(SampleFiles.Resolve("sine.mp3"));

        await engine.LoadAsync(media);

        Assert.Equal(PlaybackState.Ready, engine.State);
        Assert.True(engine.Duration > TimeSpan.Zero);
        Assert.Single(engine.Metadata.AudioTracks);
        Assert.Empty(engine.Metadata.VideoTracks);
    }

    [Fact]
    public async Task Load_mp4_enters_Ready_with_video_and_audio_tracks()
    {
        using var engine = CreateEngine();
        var media = MediaFile.FromPath(SampleFiles.Resolve("testcard.mp4"));

        await engine.LoadAsync(media);

        Assert.Equal(PlaybackState.Ready, engine.State);
        Assert.True(engine.Duration > TimeSpan.Zero);
        Assert.NotEmpty(engine.Metadata.VideoTracks);
        Assert.NotEmpty(engine.Metadata.AudioTracks);
        Assert.Contains("x", engine.Metadata.VideoTracks[0].Resolution ?? string.Empty);
    }

    [Fact]
    public async Task Load_mkv_multi_audio_reports_multiple_audio_tracks()
    {
        using var engine = CreateEngine();
        var media = MediaFile.FromPath(SampleFiles.Resolve("multi-audio.mkv"));

        await engine.LoadAsync(media);

        Assert.Equal(PlaybackState.Ready, engine.State);
        Assert.True(engine.Metadata.AudioTracks.Count >= 2,
            $"Expected 2+ audio tracks, got {engine.Metadata.AudioTracks.Count}.");
    }

    [Fact]
    public async Task Load_nonexistent_path_enters_Error_and_raises_event()
    {
        using var engine = CreateEngine();
        PlaybackErrorEventArgs? raised = null;
        engine.ErrorOccurred += (_, e) => raised = e;

        var missing = Path.Combine(Path.GetTempPath(), $"playsphere_{Guid.NewGuid():N}.mp4");
        await engine.LoadAsync(MediaFile.FromPath(missing));

        Assert.Equal(PlaybackState.Error, engine.State);
        Assert.NotNull(raised);
        Assert.False(string.IsNullOrWhiteSpace(raised.Message));
    }

    [Fact]
    public async Task Corrupted_load_recovers_with_next_load()
    {
        using var engine = CreateEngine();
        PlaybackErrorEventArgs? raised = null;
        engine.ErrorOccurred += (_, e) => raised = e;

        var corrupted = MediaFile.FromPath(SampleFiles.Resolve("corrupted.mp4"));
        await engine.LoadAsync(corrupted);

        Assert.True(engine.State is PlaybackState.Error or PlaybackState.Ready,
            $"Corrupted file should fail or (leniently) load; state was {engine.State}.");

        var valid = MediaFile.FromPath(SampleFiles.Resolve("sine.mp3"));
        await engine.LoadAsync(valid);

        Assert.Equal(PlaybackState.Ready, engine.State);
        Assert.Single(engine.Metadata.AudioTracks);
    }

    [Fact]
    public async Task Load_publishes_state_and_tracks_events()
    {
        using var engine = CreateEngine();
        PlaybackTracksChangedEventArgs? tracks = null;
        engine.TracksChanged += (_, e) => tracks = e;

        var media = MediaFile.FromPath(SampleFiles.Resolve("testcard.mp4"));
        await engine.LoadAsync(media);

        Assert.NotNull(tracks);
        Assert.NotEmpty(tracks.Metadata.VideoTracks);
        Assert.Contains(PlaybackState.Loading, _transitions);
        Assert.Contains(PlaybackState.Ready, _transitions);
    }
}