namespace PlaySphere.Domain.Media;

/// <summary>
/// Pure decision logic for the stereo-balance A/V sync strategy
/// (<c>docs/design/stereo-balance-av-sync.md</c>, ADR-0011 Stage 2). No process, engine or timer
/// dependency — this is the "should the secondary audio path be restarted at a corrected
/// position" decision only, kept separately unit-testable from the (not-yet-wired) live
/// FFmpeg/NAudio routing that will call it.
/// </summary>
public static class StereoBalanceSyncPolicy
{
    /// <summary>
    /// The interval between drift checks while the secondary balance-audio path is running, per
    /// the design note's proposed cadence.
    /// </summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The maximum tolerated divergence between the engine's video position and the secondary
    /// audio path's own elapsed position before a resync (restart) is triggered, per the design
    /// note's accepted 150ms tolerance.
    /// </summary>
    public static readonly TimeSpan DriftThreshold = TimeSpan.FromMilliseconds(150);

    /// <summary>
    /// Decides whether the secondary balance-audio path has drifted far enough from the engine's
    /// authoritative video position to warrant a restart. A negative duration on either side is
    /// treated as zero (defensive against an uninitialized/negative timestamp) rather than
    /// throwing, since this feeds a live playback loop where a transient bad reading must never
    /// take the player down.
    /// </summary>
    public static bool ShouldResync(TimeSpan enginePosition, TimeSpan audioPathPosition)
    {
        var engine = enginePosition < TimeSpan.Zero ? TimeSpan.Zero : enginePosition;
        var audio = audioPathPosition < TimeSpan.Zero ? TimeSpan.Zero : audioPathPosition;
        var drift = engine > audio ? engine - audio : audio - engine;
        return drift > DriftThreshold;
    }
}
