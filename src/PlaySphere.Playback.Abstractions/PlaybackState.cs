namespace PlaySphere.Playback.Abstractions;

/// <summary>
/// Playback state machine per PICKLE.md §16:
/// EMPTY → LOADING → READY → PLAYING ↔ PAUSED → STOPPED (Error must be recoverable).
/// </summary>
public enum PlaybackState
{
    /// <summary>No media loaded.</summary>
    Empty = 0,

    /// <summary>Media is being loaded/decoded into the engine.</summary>
    Loading,

    /// <summary>Media loaded, ready to play (or paused at start).</summary>
    Ready,

    /// <summary>Playback is advancing.</summary>
    Playing,

    /// <summary>Network playback is buffering / stalled while filling its buffer (raised by libVLC
    /// during stream startup and on playback stalls). Temporary; the engine moves on to Playing.</summary>
    Buffering,

    /// <summary>Playback is paused; position is retained.</summary>
    Paused,

    /// <summary>Playback stopped and unloaded from the playback stage.</summary>
    Stopped,

    /// <summary>A load or playback error occurred. The engine must remain usable:
    /// a subsequent load must succeed without restarting the app.</summary>
    Error,
}