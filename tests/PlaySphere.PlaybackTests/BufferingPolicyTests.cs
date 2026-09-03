using PlaySphere.Playback.Abstractions;
using PlaySphere.Playback.LibVLC;
using Xunit;

namespace PlaySphere.PlaybackTests;

/// <summary>
/// Regression tests for the libVLC Buffering-event state policy (2026-09-02).
///
/// libVLC re-raises Buffering with cache = 100 throughout active playback (audio output warm-up,
/// ES changes) and does NOT re-fire Playing each time. If the engine unconditionally transitioned
/// to Buffering on every event, a playing session would be demoted to "Buffering" and stay there —
/// the play button sticks on "play", Pause becomes unreachable, and the UI chip reads "Buffering"
/// even for local media. These tests pin the policy: cache &gt;= 100 must never demote an active
/// Playing/Paused session; it only lifts a pending Buffering back to Playing.
/// </summary>
public sealed class BufferingPolicyTests
{
    [Theory]
    [InlineData(0f)]
    [InlineData(50f)]
    [InlineData(99f)]
    public void Cache_below_100_surfaces_buffering_state(float cache)
    {
        Assert.Equal(PlaybackState.Buffering, LibVlcPlaybackEngine.EvaluateBuffering(PlaybackState.Playing, cache));
    }

    [Fact]
    public void Cache_100_while_playing_keeps_playing()
    {
        // The exact regression: repeated Buffering(100) mid-playback must not demote Playing.
        Assert.Null(LibVlcPlaybackEngine.EvaluateBuffering(PlaybackState.Playing, 100f));
    }

    [Fact]
    public void Cache_100_while_paused_keeps_paused()
    {
        Assert.Null(LibVlcPlaybackEngine.EvaluateBuffering(PlaybackState.Paused, 100f));
    }

    [Fact]
    public void Cache_100_after_pending_buffering_restores_playing()
    {
        // A stall dropped us to Buffering; the buffer refilled → resume Playing (libVLC will not
        // necessarily re-raise the Playing event, so we must lift it ourselves).
        Assert.Equal(PlaybackState.Playing, LibVlcPlaybackEngine.EvaluateBuffering(PlaybackState.Buffering, 100f));
    }

    [Theory]
    [InlineData(PlaybackState.Ready)]
    [InlineData(PlaybackState.Loading)]
    [InlineData(PlaybackState.Stopped)]
    [InlineData(PlaybackState.Error)]
    public void Cache_below_100_from_suspended_states_surfaces_buffering(PlaybackState current)
    {
        Assert.Equal(PlaybackState.Buffering, LibVlcPlaybackEngine.EvaluateBuffering(current, 1f));
    }

    [Theory]
    [InlineData(PlaybackState.Empty)]
    [InlineData(PlaybackState.Ready)]
    [InlineData(PlaybackState.Loading)]
    [InlineData(PlaybackState.Stopped)]
    [InlineData(PlaybackState.Error)]
    public void Cache_100_from_non_active_states_leaves_state_untouched(PlaybackState current)
    {
        Assert.Null(LibVlcPlaybackEngine.EvaluateBuffering(current, 100f));
    }
}