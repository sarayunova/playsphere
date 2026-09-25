using PlaySphere.Domain.Media;
using Xunit;

namespace PlaySphere.UnitTests;

/// <summary>
/// Pure tests for the stereo-balance A/V drift-correction decision (ADR-0011 Stage 2, see
/// docs/design/stereo-balance-av-sync.md). No live process/timer wiring exists yet.
/// </summary>
public sealed class StereoBalanceSyncPolicyTests
{
    [Fact]
    public void Threshold_and_interval_match_the_design_note()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(150), StereoBalanceSyncPolicy.DriftThreshold);
        Assert.Equal(TimeSpan.FromSeconds(2), StereoBalanceSyncPolicy.CheckInterval);
    }

    [Fact]
    public void No_drift_does_not_require_resync()
    {
        var position = TimeSpan.FromSeconds(30);
        Assert.False(StereoBalanceSyncPolicy.ShouldResync(position, position));
    }

    [Fact]
    public void Drift_within_threshold_does_not_require_resync()
    {
        var engine = TimeSpan.FromSeconds(30);
        var audio = engine + TimeSpan.FromMilliseconds(100);
        Assert.False(StereoBalanceSyncPolicy.ShouldResync(engine, audio));
    }

    [Fact]
    public void Drift_exactly_at_threshold_does_not_require_resync()
    {
        var engine = TimeSpan.FromSeconds(30);
        var audio = engine + StereoBalanceSyncPolicy.DriftThreshold;
        Assert.False(StereoBalanceSyncPolicy.ShouldResync(engine, audio));
    }

    [Fact]
    public void Drift_beyond_threshold_requires_resync_when_audio_ahead()
    {
        var engine = TimeSpan.FromSeconds(30);
        var audio = engine + TimeSpan.FromMilliseconds(151);
        Assert.True(StereoBalanceSyncPolicy.ShouldResync(engine, audio));
    }

    [Fact]
    public void Drift_beyond_threshold_requires_resync_when_audio_behind()
    {
        var engine = TimeSpan.FromSeconds(30);
        var audio = engine - TimeSpan.FromMilliseconds(151);
        Assert.True(StereoBalanceSyncPolicy.ShouldResync(engine, audio));
    }

    [Fact]
    public void Negative_positions_are_treated_as_zero_rather_than_throwing()
    {
        var result = StereoBalanceSyncPolicy.ShouldResync(TimeSpan.FromSeconds(-5), TimeSpan.FromSeconds(-5));
        Assert.False(result);
    }
}
