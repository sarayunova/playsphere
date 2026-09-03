using PlaySphere.Domain.Media;

namespace PlaySphere.Playback.Abstractions;

public sealed class PlaybackStateChangedEventArgs(PlaybackState previous, PlaybackState current) : EventArgs
{
    public PlaybackState Previous { get; } = previous;
    public PlaybackState Current { get; } = current;
}

public sealed class PlaybackPositionChangedEventArgs(TimeSpan position) : EventArgs
{
    public TimeSpan Position { get; } = position;
}

public sealed class PlaybackEndedEventArgs(TimeSpan? finalPosition = null) : EventArgs
{
    public TimeSpan? FinalPosition { get; } = finalPosition;
}

public sealed class PlaybackErrorEventArgs(string message, Exception? exception) : EventArgs
{
    public string Message { get; } = message;
    public Exception? Exception { get; } = exception;
}

/// <summary>
/// Raised when the set of available tracks for a loaded media changes (e.g. after load).
/// </summary>
public sealed class PlaybackTracksChangedEventArgs(MediaMetadata metadata) : EventArgs
{
    public MediaMetadata Metadata { get; } = metadata;
}

/// <summary>
/// Raised when equalizer settings (enabled, preamp, band gains, or preset) change.
/// </summary>
public sealed class PlaybackEqualizerChangedEventArgs(EqualizerConfiguration configuration) : EventArgs
{
    public EqualizerConfiguration Configuration { get; } = configuration;
}