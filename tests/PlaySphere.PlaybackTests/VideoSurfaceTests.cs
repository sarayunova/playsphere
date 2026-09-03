using Microsoft.Extensions.Logging.Abstractions;
using PlaySphere.Domain.Media;
using PlaySphere.Playback.Abstractions;
using PlaySphere.Playback.LibVLC;
using Xunit;

namespace PlaySphere.PlaybackTests;

[Collection(LibVlcCollection.Name)]
public sealed class VideoSurfaceTests
{
    private readonly LibVlcFixture _fixture;

    public VideoSurfaceTests(LibVlcFixture fixture) => _fixture = fixture;

    private sealed class RecordingSurface : IVideoSurface
    {
        public RecordingSurface(string[]? swapChainOptions = null) => SwapChainOptions = swapChainOptions;
        public string[]? SwapChainOptions { get; }
        public object? CurrentRenderer { get; private set; }
        public int SetRendererCount { get; private set; }
        public int ClearRendererCount { get; private set; }

        public void SetRenderer(object renderer)
        {
            CurrentRenderer = renderer;
            SetRendererCount++;
        }

        public void ClearRenderer()
        {
            CurrentRenderer = null;
            ClearRendererCount++;
        }
    }

    [Fact]
    public async Task Attach_binds_renderer_and_marks_surface_attached()
    {
        using var engine = _fixture.CreateEngine();
        var surface = new RecordingSurface();

        Assert.False(engine.IsVideoSurfaceAttached);

        await engine.AttachVideoSurfaceAsync(surface);

        Assert.True(engine.IsVideoSurfaceAttached);
        Assert.NotNull(surface.CurrentRenderer);
        Assert.Equal(1, surface.SetRendererCount);
    }

    [Fact]
    public async Task Reattaching_same_surface_is_a_noop_for_the_engine()
    {
        using var engine = _fixture.CreateEngine();
        var surface = new RecordingSurface();

        await engine.AttachVideoSurfaceAsync(surface);
        var rendererAfterFirst = surface.CurrentRenderer;

        await engine.AttachVideoSurfaceAsync(surface);

        Assert.True(engine.IsVideoSurfaceAttached);
        Assert.Same(rendererAfterFirst, surface.CurrentRenderer);
        // Second attach should not re-set the renderer on the already-bound surface.
        Assert.Equal(1, surface.SetRendererCount);
    }

    [Fact]
    public async Task Attaching_different_surface_clears_previous_and_binds_new()
    {
        using var engine = _fixture.CreateEngine();
        var first = new RecordingSurface();
        var second = new RecordingSurface();

        await engine.AttachVideoSurfaceAsync(first);
        await engine.AttachVideoSurfaceAsync(second);

        Assert.Equal(1, first.ClearRendererCount);
        Assert.Null(first.CurrentRenderer);
        Assert.NotNull(second.CurrentRenderer);
        Assert.True(engine.IsVideoSurfaceAttached);
    }

    [Fact]
    public async Task Detach_clears_renderer_and_marks_not_attached()
    {
        using var engine = _fixture.CreateEngine();
        var surface = new RecordingSurface();
        await engine.AttachVideoSurfaceAsync(surface);

        await engine.DetachVideoSurfaceAsync();

        Assert.False(engine.IsVideoSurfaceAttached);
        Assert.Equal(1, surface.ClearRendererCount);
        Assert.Null(surface.CurrentRenderer);
    }

    [Fact]
    public async Task Detach_with_no_attached_surface_is_safe()
    {
        using var engine = _fixture.CreateEngine();

        await engine.DetachVideoSurfaceAsync();

        Assert.False(engine.IsVideoSurfaceAttached);
    }

    [Fact]
    public async Task Attach_null_surface_throws()
    {
        using var engine = _fixture.CreateEngine();

        await Assert.ThrowsAsync<ArgumentNullException>(() => engine.AttachVideoSurfaceAsync(null!));
    }

    /// <summary>
    /// Regression (the real-app 'MediaPlayer.Play() returned false' defect): attaching a
    /// swap-chain video surface rebuilds the LibVLC/MediaPlayer (construction-time swap-chain
    /// options) into a fresh player that has NO media. Before the fix the loaded media was
    /// dropped, so a subsequent Play() ran on an empty input and returned false. After the fix
    /// the rebuild re-attaches the currently-loaded media and playback must start normally.
    /// </summary>
    [Fact]
    public async Task Attaching_swapchain_surface_rebuilds_renderer_but_loaded_media_still_plays()
    {
        using var engine = _fixture.CreateEngine();
        var media = MediaFile.FromPath(SampleFiles.Resolve("testcard.mp4"));
        await engine.LoadAsync(media);
        Assert.Equal(PlaybackState.Ready, engine.State);

        // Non-empty swap-chain options differ from the engine's default (no swap-chain), which is
        // exactly what forces RebuildRenderer on attach — the path that used to drop the media.
        var surface = new RecordingSurface(new[] { "--winrt-swapchain=0", "--winrt-d3dcontext=0" });

        await engine.AttachVideoSurfaceAsync(surface);

        Assert.True(engine.IsVideoSurfaceAttached);
        Assert.NotNull(surface.CurrentRenderer);

        await engine.PlayAsync();

        var reached = await WaitForStateAsync(engine, PlaybackState.Playing);
        Assert.True(reached, $"Engine did not reach Playing after swap-chain rebuild; state: {engine.State}");
    }

    /// <summary>
    /// Regression (the real-app "returning to the player resets to the start and pauses" defect):
    /// attaching a video surface with different swap-chain options while the engine is actively
    /// Playing forces <c>RebuildRenderer</c>, which silently stops the old native player (its Stopped
    /// event is deliberately unwired first, since the rebuild itself isn't a user-initiated stop).
    /// Before this fix the engine landed the session stopped-at-position-0; after the fix a genuinely
    /// Playing session is resumed on the rebuilt renderer so the state machine (and every UI bound to
    /// it — the "Now Playing" bar, the player page) reports/behaves as still playing, not as having
    /// silently halted.
    /// </summary>
    [Fact]
    public async Task Rebuilding_renderer_while_playing_resumes_playback()
    {
        using var engine = _fixture.CreateEngine();
        var media = MediaFile.FromPath(SampleFiles.Resolve("testcard.mp4"));
        await engine.LoadAsync(media);

        var firstSurface = new RecordingSurface(new[] { "--winrt-swapchain=0", "--winrt-d3dcontext=0" });
        await engine.AttachVideoSurfaceAsync(firstSurface);

        await engine.PlayAsync();
        Assert.True(await WaitForStateAsync(engine, PlaybackState.Playing), "Did not reach Playing before rebuild.");

        // A different swap-chain surface (e.g. re-entering the player page constructs a brand-new
        // VideoView) forces another RebuildRenderer while genuinely Playing.
        var secondSurface = new RecordingSurface(new[] { "--winrt-swapchain=1", "--winrt-d3dcontext=1" });
        await engine.AttachVideoSurfaceAsync(secondSurface);

        // The rebuilt renderer must resume, not halt: the engine reaches Playing again, never a
        // stale "stopped at the start" state.
        Assert.True(await WaitForStateAsync(engine, PlaybackState.Playing),
            $"Engine did not resume Playing after rebuild; state: {engine.State}");
        Assert.Equal(PlaybackState.Playing, engine.State);
    }

    /// <summary>
    /// Regression companion to the resume test: beyond not halting, the position captured from the
    /// pre-rebuild renderer must be carried to the rebuilt one immediately (surfaced via
    /// PositionChanged). Real native position readback is NOT asserted here — libVLC silently drops a
    /// pre-Play position on a freshly-parsed media, and every attempt to force it (the ":start-time"
    /// media option, or a deferred Time set on the native event thread) reproduced a native host
    /// crash, so position restoration is deliberately best-effort only (see AGENTS.md). What IS
    /// guaranteed by the fix is that playback resumes instead of halting at the start.
    /// </summary>
    [Fact]
    public async Task Rebuild_while_playing_carries_position_and_resumes()
    {
        using var engine = _fixture.CreateEngine();
        var media = MediaFile.FromPath(SampleFiles.Resolve("testcard.mp4"));
        await engine.LoadAsync(media);

        var firstSurface = new RecordingSurface(new[] { "--winrt-swapchain=0", "--winrt-d3dcontext=0" });
        await engine.AttachVideoSurfaceAsync(firstSurface);

        await engine.PlayAsync();
        Assert.True(await WaitForStateAsync(engine, PlaybackState.Playing), "Did not reach Playing before seek.");

        var sought = TimeSpan.FromSeconds(2);
        await engine.SeekAsync(sought);
        await Task.Delay(300);

        // Capture the PositionChanged the rebuild must fire carrying the pre-rebuild position.
        TimeSpan? carried = null;
        engine.PositionChanged += (_, e) => carried ??= e.Position;

        var secondSurface = new RecordingSurface(new[] { "--winrt-swapchain=1", "--winrt-d3dcontext=1" });
        await engine.AttachVideoSurfaceAsync(secondSurface);

        // Playback resumes (never halts stopped-at-0), and the carried position was surfaced.
        Assert.True(await WaitForStateAsync(engine, PlaybackState.Playing),
            $"Engine did not resume Playing after rebuild; state: {engine.State}");
        Assert.NotNull(carried);
        Assert.True(carried!.Value >= sought - TimeSpan.FromMilliseconds(1500),
            $"Rebuild did not surface the carried position; expected >= {sought}, got {carried.Value}.");
    }

    /// <summary>
    /// A rebuild triggered while the session is Paused must stay paused (not silently resume), and
    /// the carried position must be surfaced via PositionChanged.
    /// </summary>
    [Fact]
    public async Task Rebuild_while_paused_stays_paused_and_carries_position()
    {
        using var engine = _fixture.CreateEngine();
        var media = MediaFile.FromPath(SampleFiles.Resolve("testcard.mp4"));
        await engine.LoadAsync(media);

        var firstSurface = new RecordingSurface(new[] { "--winrt-swapchain=0", "--winrt-d3dcontext=0" });
        await engine.AttachVideoSurfaceAsync(firstSurface);

        await engine.PlayAsync();
        Assert.True(await WaitForStateAsync(engine, PlaybackState.Playing), "Did not reach Playing before pause.");

        var sought = TimeSpan.FromSeconds(2);
        await engine.SeekAsync(sought);
        await engine.PauseAsync();
        Assert.True(await WaitForStateAsync(engine, PlaybackState.Paused), "Did not reach Paused before rebuild.");

        TimeSpan? carried = null;
        engine.PositionChanged += (_, e) => carried ??= e.Position;

        var secondSurface = new RecordingSurface(new[] { "--winrt-swapchain=1", "--winrt-d3dcontext=1" });
        await engine.AttachVideoSurfaceAsync(secondSurface);

        // Not silently resumed; stays paused, and the carried position was surfaced.
        Assert.True(await WaitForStateAsync(engine, PlaybackState.Paused),
            $"Engine did not settle on Paused after rebuild; state: {engine.State}");
        Assert.NotNull(carried);
        Assert.True(carried!.Value >= sought - TimeSpan.FromMilliseconds(1500),
            $"Rebuild did not surface the carried position; expected >= {sought}, got {carried.Value}.");
    }

    /// <summary>
    /// Regression guard for "returning to the player restarts the music from the beginning": a video
    /// surface attach that would otherwise trigger a native renderer rebuild must NOT do so for
    /// audio-only media, because rebuilding tears down the active audio session (restarting the music)
    /// with no video to route to the new surface. With the fix, attaching a differently-optioned
    /// surface to a playing audio-only session is a plain rebind — audio keeps flowing and the
    /// position is preserved verbatim. Without the fix this test fails: the rebuild resets the
    /// position to ~0 and interrupts playback.
    /// </summary>
    [Fact]
    public async Task Attaching_surface_to_audio_only_session_does_not_rebuild_or_reset_position()
    {
        using var engine = _fixture.CreateEngine();
        var media = MediaFile.FromPath(SampleFiles.Resolve("sine.mp3"));
        await engine.LoadAsync(media);

        // Sanity: this fixture is genuinely audio-only, so the audio-only fast path applies.
        Assert.Empty(engine.Metadata.VideoTracks);

        var firstSurface = new RecordingSurface(new[] { "--winrt-swapchain=0", "--winrt-d3dcontext=0" });
        await engine.AttachVideoSurfaceAsync(firstSurface);

        await engine.PlayAsync();
        Assert.True(await WaitForStateAsync(engine, PlaybackState.Playing), "Did not reach Playing.");

        var sought = TimeSpan.FromSeconds(1);
        await engine.SeekAsync(sought);
        await Task.Delay(250);
        Assert.True(engine.Position >= TimeSpan.FromMilliseconds(500),
            $"Expected to be playing past the start; got {engine.Position}.");

        // Attaching a differently-optioned surface must be a no-op rebuild-wise for audio-only media.
        var secondSurface = new RecordingSurface(new[] { "--winrt-swapchain=1", "--winrt-d3dcontext=1" });
        await engine.AttachVideoSurfaceAsync(secondSurface);

        // Playback was NOT interrupted and the position was NOT reset toward the start — the thing
        // returning to the player used to do.
        Assert.True(await WaitForStateAsync(engine, PlaybackState.Playing),
            $"Audio-only re-attach interrupted playback; state: {engine.State}");
        Assert.True(engine.Position >= TimeSpan.FromMilliseconds(500),
            $"Audio-only re-attach reset position to the start; got {engine.Position}.");
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
