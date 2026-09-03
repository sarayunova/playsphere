namespace PlaySphere.Application.Preferences;

/// <summary>
/// Persistence boundary for lightweight user playback preferences (PICKLE.md). UI and modules
/// depend on this contract, never on the concrete JSON/file implementation
/// (<c>PlaySphere.Infrastructure</c>) — mirrors how playback goes through
/// <c>IMediaPlaybackEngine</c> instead of calling libVLC directly. Implementations must be
/// defensive against a missing or corrupted backing file (treat it as "no preferences").
/// </summary>
public interface IPreferencesStore
{
    /// <summary>
    /// Ordered list of preferred audio languages (most preferred first), each a bare tag like
    /// <c>"en"</c> or a region tag like <c>"en-US"</c>. Empty when nothing is configured.
    /// </summary>
    IReadOnlyList<string> GetPreferredAudioLanguages();

    /// <summary>Replaces the ordered list of preferred audio languages, persisted immediately.</summary>
    void SetPreferredAudioLanguages(IReadOnlyList<string> languages);
}
