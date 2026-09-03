namespace PlaySphere.Domain.Playback;

/// <summary>
/// Repeat behavior for a playback session (PICKLE.md §25 "repeat one, repeat all").
/// </summary>
public enum PlaybackRepeat
{
    /// <summary>Stop advancing once the end of the queue is reached.</summary>
    Off,

    /// <summary>Repeat the current item indefinitely (next/previous keep the same item).</summary>
    One,

    /// <summary>Wrap from the last item back to the first (and from first to last on previous).</summary>
    All,
}
