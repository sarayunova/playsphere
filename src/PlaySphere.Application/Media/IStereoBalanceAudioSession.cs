using PlaySphere.Domain.Media;

namespace PlaySphere.Application.Media;

/// <summary>
/// The live stereo-balance secondary audio path (ADR-0011, docs/design/stereo-balance-av-sync.md).
/// Implemented by the isolated ffmpeg+NAudio session in <c>PlaySphere.Audio</c>; the UI/VM depend
/// only on this contract, never on the concrete process/output-device plumbing (mirrors
/// <c>IVisualizerAudioSource</c>'s boundary discipline for the same reason: this is a Windows-only,
/// process-spawning implementation).
/// </summary>
public interface IStereoBalanceAudioSession
{
    /// <summary>True while the secondary audio path is running.</summary>
    bool IsActive { get; }

    /// <summary>The balance most recently applied (whether or not the session is currently active).</summary>
    StereoBalance CurrentBalance { get; }

    /// <summary>
    /// Starts the secondary audio path for <paramref name="filePath"/> at
    /// <paramref name="getEnginePosition"/>'s current value, applying <paramref name="balance"/>.
    /// Best-effort: returns false (never throws) if the secondary path is unavailable or fails to
    /// start; the caller should treat that as "balance audio unavailable, continue with normal
    /// playback" and never mute the primary engine output on a false result.
    /// </summary>
    Task<bool> StartAsync(string filePath, StereoBalance balance, Func<TimeSpan> getEnginePosition, CancellationToken cancellationToken = default);

    /// <summary>Applies a new balance value to the already-running session.</summary>
    Task<bool> UpdateBalanceAsync(StereoBalance balance, Func<TimeSpan> getEnginePosition, CancellationToken cancellationToken = default);

    /// <summary>Tells the session the engine transport jumped to a new position (a user seek, etc.).</summary>
    Task<bool> NotifyEngineSeekedAsync(TimeSpan newPosition, CancellationToken cancellationToken = default);

    /// <summary>Stops the secondary audio path entirely. Safe to call when inactive.</summary>
    Task StopAsync();
}
