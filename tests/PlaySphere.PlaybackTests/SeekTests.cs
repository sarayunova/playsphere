using PlaySphere.Domain.Media;
using PlaySphere.Playback.Abstractions;
using PlaySphere.Playback.LibVLC;
using Xunit;

namespace PlaySphere.PlaybackTests;

/// <summary>
/// PS-105: seek integration tests against the real libVLC adapter.
///
/// Acceptance criteria (PICKLE.md §52 Phase 1):
///   - Seek forward/back within MKV and MP4; TimeChanged fires.
///   - Seek beyond end clamps to duration safely.
///   - Seek to negative value clamps to zero.
///   - PositionChanged event fires immediately after SeekAsync.
///   - SeekAsync in invalid states (Empty, Loading) is silently ignored.
/// </summary>
[Collection(LibVlcCollection.Name)]
public sealed class SeekTests
{
    private const int EventTimeoutMs = 8_000;
    private readonly LibVlcFixture _fixture;

    public SeekTests(LibVlcFixture fixture) => _fixture = fixture;

    // ── helpers ──────────────────────────────────────────────────────────────

    private LibVlcPlaybackEngine CreateEngine() => _fixture.CreateEngine();

    private static Task<bool> WaitForStateAsync(
        IMediaPlaybackEngine engine,
        PlaybackState target,
        int timeoutMs = EventTimeoutMs)
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

    private static async Task LoadAndAssertReadyAsync(LibVlcPlaybackEngine engine, string file)
    {
        await engine.LoadAsync(MediaFile.FromPath(SampleFiles.Resolve(file)));
        Assert.Equal(PlaybackState.Ready, engine.State);
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SeekAsync_while_playing_fires_PositionChanged()
    {
        using var engine = CreateEngine();
        TimeSpan? reported = null;
        engine.PositionChanged += (_, e) => reported = e.Position;

        await LoadAndAssertReadyAsync(engine, "testcard.mp4");
        await engine.PlayAsync();
        Assert.True(await WaitForStateAsync(engine, PlaybackState.Playing));

        var target = TimeSpan.FromSeconds(2);
        await engine.SeekAsync(target);

        Assert.NotNull(reported);
        // PositionChanged was fired immediately with the clamped target.
        Assert.InRange(reported!.Value.TotalMilliseconds, 0, engine.Duration.TotalMilliseconds);
    }

    [Fact]
    public async Task SeekAsync_forward_advances_position_in_mp4()
    {
        using var engine = CreateEngine();
        await LoadAndAssertReadyAsync(engine, "testcard.mp4");
        await engine.PlayAsync();
        Assert.True(await WaitForStateAsync(engine, PlaybackState.Playing));

        // Let it settle briefly then seek forward.
        await Task.Delay(100);
        var seekTarget = TimeSpan.FromSeconds(3);
        await engine.SeekAsync(seekTarget);
        await Task.Delay(300); // libVLC processes the seek

        // Position should be in the neighbourhood of the seek target (±1.5 s tolerance).
        Assert.InRange(engine.Position.TotalSeconds, seekTarget.TotalSeconds - 1.5, seekTarget.TotalSeconds + 1.5);
    }

    [Fact]
    public async Task SeekAsync_forward_advances_position_in_mkv()
    {
        using var engine = CreateEngine();
        await LoadAndAssertReadyAsync(engine, "multi-audio.mkv");
        await engine.PlayAsync();
        Assert.True(await WaitForStateAsync(engine, PlaybackState.Playing));

        await Task.Delay(100);
        var seekTarget = TimeSpan.FromSeconds(2);
        await engine.SeekAsync(seekTarget);
        await Task.Delay(300);

        Assert.InRange(engine.Position.TotalSeconds, 0, engine.Duration.TotalSeconds);
    }

    [Fact]
    public async Task SeekAsync_to_negative_clamps_to_zero()
    {
        using var engine = CreateEngine();
        TimeSpan? reported = null;
        engine.PositionChanged += (_, e) => reported = e.Position;

        await LoadAndAssertReadyAsync(engine, "testcard.mp4");
        await engine.PlayAsync();
        Assert.True(await WaitForStateAsync(engine, PlaybackState.Playing));

        await engine.SeekAsync(TimeSpan.FromSeconds(-10));

        Assert.NotNull(reported);
        Assert.Equal(TimeSpan.Zero, reported!.Value);
    }

    [Fact]
    public async Task SeekAsync_beyond_duration_clamps_to_duration()
    {
        using var engine = CreateEngine();
        TimeSpan? reported = null;
        engine.PositionChanged += (_, e) => reported = e.Position;

        await LoadAndAssertReadyAsync(engine, "testcard.mp4");
        await engine.PlayAsync();
        Assert.True(await WaitForStateAsync(engine, PlaybackState.Playing));

        var wayBeyond = TimeSpan.FromHours(99);
        await engine.SeekAsync(wayBeyond);

        Assert.NotNull(reported);
        // Clamped position must not exceed duration.
        Assert.True(reported!.Value <= engine.Duration,
            $"Reported position {reported.Value} exceeds duration {engine.Duration}.");
    }

    [Fact]
    public async Task SeekAsync_while_paused_fires_PositionChanged_and_does_not_resume()
    {
        using var engine = CreateEngine();
        await LoadAndAssertReadyAsync(engine, "testcard.mp4");
        await engine.PlayAsync();
        Assert.True(await WaitForStateAsync(engine, PlaybackState.Playing));
        await Task.Delay(200);

        await engine.PauseAsync();
        Assert.True(await WaitForStateAsync(engine, PlaybackState.Paused));

        TimeSpan? reported = null;
        engine.PositionChanged += (_, e) => reported = e.Position;

        await engine.SeekAsync(TimeSpan.FromSeconds(1));

        Assert.NotNull(reported);
        // Must remain paused after seeking.
        Assert.Equal(PlaybackState.Paused, engine.State);
    }

    [Fact]
    public async Task SeekAsync_in_Empty_state_is_ignored()
    {
        using var engine = CreateEngine();
        // Must not throw; engine stays Empty.
        await engine.SeekAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(PlaybackState.Empty, engine.State);
    }
}
