namespace PlaySphere.UI.Models;

/// <summary>
/// Identifies which slide-out drawer or overlay is currently active in the player screen.
/// </summary>
public enum PlayerDrawerKind
{
    None = 0,
    AudioTracks,
    SubtitleTracks,
    PlaybackSpeed,
    MediaInfo,
    Chapters,
    Bookmarks,
    Equalizer,
}
