namespace PlaySphere.Playback.Abstractions;

/// <summary>
/// Hard bounds for user-facing playback adjustments. libVLC itself may allow device-wide
/// ranges; the engine clamps to these product limits so UI and tests share one contract.
/// </summary>
public static class PlaybackConstraints
{
    /// <summary>Slowest supported playback speed.</summary>
    public const double MinRate = 0.25;

    /// <summary>Fastest supported playback speed.</summary>
    public const double MaxRate = 4.0;

    /// <summary>Minimum volume (0 = absolute minimum, effectively silent).</summary>
    public const int MinVolume = 0;

    /// <summary>Maximum volume.</summary>
    public const int MaxVolume = 100;

    /// <summary>Maximum subtitle lead time (subtitle appears earlier, -60 seconds).</summary>
    public static readonly TimeSpan MinSubtitleDelay = TimeSpan.FromSeconds(-60);

    /// <summary>Maximum subtitle lag time (subtitle appears later, +60 seconds).</summary>
    public static readonly TimeSpan MaxSubtitleDelay = TimeSpan.FromSeconds(60);

    /// <summary>Maximum audio advance (-30 seconds).</summary>
    public static readonly TimeSpan MinAudioDelay = TimeSpan.FromSeconds(-30);

    /// <summary>Maximum audio delay (+30 seconds).</summary>
    public static readonly TimeSpan MaxAudioDelay = TimeSpan.FromSeconds(30);

    /// <summary>Minimum gain for an equalizer band or preamp (-20.0 dB).</summary>
    public const float MinEqualizerGainDb = -20.0f;

    /// <summary>Maximum gain for an equalizer band or preamp (+20.0 dB).</summary>
    public const float MaxEqualizerGainDb = 20.0f;

    /// <summary>Minimum subtitle text scale (25%), per libVLC's <c>sub-text-scale</c> range.</summary>
    public const int MinSubtitleTextScalePercent = 25;

    /// <summary>Maximum subtitle text scale (500%), per libVLC's <c>sub-text-scale</c> range.</summary>
    public const int MaxSubtitleTextScalePercent = 500;

    /// <summary>Minimum subtitle color value (0x000000, black) in 0xRRGGBB.</summary>
    public const int MinSubtitleColor = 0x000000;

    /// <summary>Maximum subtitle color value (0xFFFFFF, white) in 0xRRGGBB.</summary>
    public const int MaxSubtitleColor = 0xFFFFFF;

    /// <summary>Minimum subtitle vertical margin in pixels (bottom edge).</summary>
    public const int MinVerticalMarginPx = 0;

    /// <summary>Maximum subtitle vertical margin in pixels (far from bottom edge).</summary>
    public const int MaxVerticalMarginPx = 1000;

    public static double ClampRate(double rate) => Math.Clamp(rate, MinRate, MaxRate);

    public static int ClampVolume(int volume) => Math.Clamp(volume, MinVolume, MaxVolume);

    public static float ClampEqualizerGain(float gainDb) => Math.Clamp(gainDb, MinEqualizerGainDb, MaxEqualizerGainDb);

    public static float ClampEqualizerPreamp(float preampDb) => Math.Clamp(preampDb, MinEqualizerGainDb, MaxEqualizerGainDb);

    public static TimeSpan ClampSubtitleDelay(TimeSpan delay)
    {
        if (delay < MinSubtitleDelay) return MinSubtitleDelay;
        if (delay > MaxSubtitleDelay) return MaxSubtitleDelay;
        return delay;
    }

    public static TimeSpan ClampAudioDelay(TimeSpan delay)
    {
        if (delay < MinAudioDelay) return MinAudioDelay;
        if (delay > MaxAudioDelay) return MaxAudioDelay;
        return delay;
    }

    public static int ClampSubtitleTextScale(int percent) =>
        Math.Clamp(percent, MinSubtitleTextScalePercent, MaxSubtitleTextScalePercent);

    public static int ClampSubtitleColor(int argb) => Math.Clamp(argb, MinSubtitleColor, MaxSubtitleColor);

    public static int ClampVerticalMarginPx(int margin) =>
        Math.Clamp(margin, MinVerticalMarginPx, MaxVerticalMarginPx);
}