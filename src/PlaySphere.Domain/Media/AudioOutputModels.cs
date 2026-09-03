namespace PlaySphere.Domain.Media;

/// <summary>An available system audio output device (PICKLE.md §18).</summary>
public sealed record AudioOutputDeviceInfo(string Id, string Name);

/// <summary>
/// Stereo/mono downmix channel modes (PICKLE.md §18 "stereo/mono downmix where supported").
/// <see cref="MonoLeft"/>/<see cref="MonoRight"/> play only that source channel on both
/// speakers — the mono-fold approach playback engines commonly expose, since true averaged
/// downmix is not a distinct engine-level mode on every backend.
/// </summary>
public enum AudioChannelMode
{
    Stereo,
    MonoLeft,
    MonoRight,
    ReverseStereo,
    Dolby,
}

/// <summary>
/// Audio enhancement toggles (PICKLE.md §18: loudness normalization, night mode). Applied via
/// the engine's audio filter chain, which is configured per-media — changes take effect on the
/// next <c>LoadAsync</c>, not instantaneously mid-playback.
/// </summary>
public sealed record AudioEnhancementOptions(bool LoudnessNormalizationEnabled, bool NightModeEnabled)
{
    public static AudioEnhancementOptions Default { get; } = new(false, false);
}
