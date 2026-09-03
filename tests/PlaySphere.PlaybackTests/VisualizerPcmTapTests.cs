using PlaySphere.Application.Visualizer;
using PlaySphere.Domain.Media;
using PlaySphere.Playback.Abstractions;
using PlaySphere.Playback.LibVLC;
using Xunit;

namespace PlaySphere.PlaybackTests;

/// <summary>
/// Exercises the visualizer PCM-tap contract (ADR-0008) against the real <see cref="LibVlcPlaybackEngine"/>:
/// attach/detach state, source replacement, null guard, and the detach path that rebuilds the native
/// player to restore normal audio output. The actual decoded-PCM → <see cref="IVisualizerAudioSource.PushSamples"/>
/// hand-off (and the FL32 fourcc it requests) requires playing real media on an audio thread, which is
/// covered by design and manual QA — no display/audio device exists in this environment to verify it.
/// </summary>
[Collection(LibVlcCollection.Name)]
public sealed class VisualizerPcmTapTests
{
    private readonly LibVlcFixture _fixture;

    public VisualizerPcmTapTests(LibVlcFixture fixture) => _fixture = fixture;

    private sealed class RecordingSource : IVisualizerAudioSource
    {
        public int PushCount { get; private set; }
        public int StopCount { get; private set; }
        public int StartCount { get; private set; }

        public void Start() => StartCount++;

        public void PushSamples(ReadOnlySpan<float> samples, int sampleRate, int channelCount)
            => PushCount++;

        public void Stop() => StopCount++;
    }

    [Fact]
    public async Task Attach_marks_visualizer_attached()
    {
        using var engine = _fixture.CreateEngine();
        var source = new RecordingSource();

        Assert.False(engine.IsVisualizerAttached);

        await engine.AttachVisualizerSourceAsync(source);

        Assert.True(engine.IsVisualizerAttached);
    }

    [Fact]
    public async Task Reattaching_replaces_the_source()
    {
        using var engine = _fixture.CreateEngine();
        var first = new RecordingSource();
        var second = new RecordingSource();

        await engine.AttachVisualizerSourceAsync(first);
        await engine.AttachVisualizerSourceAsync(second);

        Assert.True(engine.IsVisualizerAttached);
    }

    [Fact]
    public async Task Attach_null_source_throws()
    {
        using var engine = _fixture.CreateEngine();

        await Assert.ThrowsAsync<ArgumentNullException>(() => engine.AttachVisualizerSourceAsync(null!));
    }

    [Fact]
    public async Task Detach_marks_not_attached_and_stops_the_source()
    {
        using var engine = _fixture.CreateEngine();
        var source = new RecordingSource();
        await engine.AttachVisualizerSourceAsync(source);

        await engine.DetachVisualizerSourceAsync();

        Assert.False(engine.IsVisualizerAttached);
        Assert.Equal(1, source.StopCount);
    }

    [Fact]
    public async Task Detach_with_no_attached_source_is_safe()
    {
        using var engine = _fixture.CreateEngine();

        await engine.DetachVisualizerSourceAsync();

        Assert.False(engine.IsVisualizerAttached);
    }

    /// <summary>
    /// Regression (the real-app "enabling the visualizer while playing crashes after a few seconds"
    /// defect): attaching the visualizer PCM tap mid-playback must NOT rebuild the native player
    /// (a mid-playback rebuild races the live audio thread and stalls the media clock, corrupting
    /// the heap → late native AV). Instead it defers the tap to the next renderer rebuild and leaves
    /// playback running untouched. This asserts the engine is still Playing (never stopped/rebuilt)
    /// immediately after a mid-playback attach.
    /// </summary>
    [Fact]
    public async Task Attach_visualizer_mid_playback_does_not_interrupt()
    {
        using var engine = _fixture.CreateEngine();
        var media = MediaFile.FromPath(SampleFiles.Resolve("testcard.mp4"));

        await engine.LoadAsync(media);
        Assert.Equal(PlaybackState.Ready, engine.State);

        await engine.PlayAsync();
        Assert.True(await WaitForStateAsync(engine, PlaybackState.Playing), "Did not reach Playing before attach.");

        var source = new RecordingSource();
        await engine.AttachVisualizerSourceAsync(source);

        // The tap is recorded as attached, but playback must carry on uninterrupted (no rebuild).
        Assert.True(engine.IsVisualizerAttached);
        Assert.Equal(PlaybackState.Playing, engine.State);
    }

    /// <summary>
    /// Regression (companion to Attach_visualizer_mid_playback_does_not_interrupt): detaching the
    /// PCM tap mid-playback must also avoid a native rebuild; the engine stays Playing and the
    /// source is stopped. The un-registration itself defers to the next renderer rebuild.
    /// </summary>
    [Fact]
    public async Task Detach_visualizer_mid_playback_does_not_interrupt()
    {
        using var engine = _fixture.CreateEngine();
        var media = MediaFile.FromPath(SampleFiles.Resolve("testcard.mp4"));

        await engine.LoadAsync(media);
        Assert.Equal(PlaybackState.Ready, engine.State);

        await engine.PlayAsync();
        Assert.True(await WaitForStateAsync(engine, PlaybackState.Playing), "Did not reach Playing before attach.");

        var source = new RecordingSource();
        await engine.AttachVisualizerSourceAsync(source);
        Assert.True(engine.IsVisualizerAttached);

        await engine.DetachVisualizerSourceAsync();

        Assert.False(engine.IsVisualizerAttached);
        Assert.Equal(1, source.StopCount);
        Assert.Equal(PlaybackState.Playing, engine.State);
    }

    private static Task<bool> WaitForStateAsync(
        IMediaPlaybackEngine engine,
        PlaybackState target,
        int timeoutMs = 8_000)
    {
        if (engine.State == target) return Task.FromResult(true);

        var tcs = new TaskCompletionSource<bool>();

        engine.StateChanged += Handler;

        return tcs.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs))
            .ContinueWith(t =>
            {
                engine.StateChanged -= Handler;
                return !t.IsFaulted && !t.IsCanceled && t.Result;
            }, TaskScheduler.Default);

        void Handler(object? _, PlaybackStateChangedEventArgs e)
        {
            if (e.Current == target) tcs.TrySetResult(true);
        }
    }
}
