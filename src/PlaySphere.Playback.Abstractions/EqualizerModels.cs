namespace PlaySphere.Playback.Abstractions;

/// <summary>
/// Represents a single frequency band of the audio equalizer.
/// </summary>
/// <param name="Index">Zero-based band index (0 to 9 for standard 10-band equalizer).</param>
/// <param name="FrequencyHz">Center frequency in Hertz (e.g. 60, 170, 310, 600, 1000, etc.).</param>
/// <param name="GainDb">Gain in decibels (-20.0 dB to +20.0 dB).</param>
public sealed record EqualizerBand(uint Index, float FrequencyHz, float GainDb);

/// <summary>
/// Represents a named equalizer preset with preamp and 10 frequency band gains.
/// </summary>
/// <param name="Name">Human-readable preset name (e.g. "Flat", "Rock", "Pop").</param>
/// <param name="PreampGainDb">Preamp gain in decibels.</param>
/// <param name="BandGainsDb">Array of 10 gain values in decibels corresponding to the 10 standard bands.</param>
public sealed record EqualizerPreset(string Name, float PreampGainDb, IReadOnlyList<float> BandGainsDb);

/// <summary>
/// Snapshot of the complete equalizer configuration state.
/// </summary>
/// <param name="IsEnabled">True if equalizer filter is active.</param>
/// <param name="PreampGainDb">Current preamp gain in decibels (-20.0 dB to +20.0 dB).</param>
/// <param name="BandGainsDb">Current gain in decibels for each of the 10 bands.</param>
/// <param name="PresetName">Name of active preset, or "Custom" / null if custom gains are set.</param>
public sealed record EqualizerConfiguration(
    bool IsEnabled,
    float PreampGainDb,
    IReadOnlyList<float> BandGainsDb,
    string? PresetName)
{
    /// <summary>Default flat equalizer configuration (disabled, 0 dB preamp, all bands 0 dB).</summary>
    public static EqualizerConfiguration Default { get; } = new(
        IsEnabled: false,
        PreampGainDb: 0.0f,
        BandGainsDb: [0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f],
        PresetName: "Flat");
}

/// <summary>
/// Catalog of standard 10-band equalizer presets and ISO frequency band definitions.
/// </summary>
public static class EqualizerPresets
{
    /// <summary>
    /// Standard 10 ISO center frequencies (Hz) used by libVLC and standard graphic equalizers:
    /// 60Hz, 170Hz, 310Hz, 600Hz, 1kHz, 3kHz, 6kHz, 12kHz, 14kHz, 16kHz.
    /// </summary>
    public static readonly IReadOnlyList<float> StandardFrequencies =
    [
        60.0f,
        170.0f,
        310.0f,
        600.0f,
        1000.0f,
        3000.0f,
        6000.0f,
        12000.0f,
        14000.0f,
        16000.0f
    ];

    private static readonly Dictionary<string, EqualizerPreset> PresetsByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Flat"] = new("Flat", 0.0f, [0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f]),
        ["Classical"] = new("Classical", 0.0f, [0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, -4.8f, -4.8f, -4.8f, -6.4f]),
        ["Club"] = new("Club", 0.0f, [0.0f, 0.0f, 1.6f, 4.0f, 4.0f, 4.0f, 2.4f, 0.0f, 0.0f, 0.0f]),
        ["Dance"] = new("Dance", 0.0f, [7.2f, 5.6f, 2.4f, 0.0f, 0.0f, -4.0f, -5.6f, -5.6f, 0.0f, 0.0f]),
        ["Full Bass"] = new("Full Bass", 0.0f, [7.2f, 5.6f, 5.6f, 3.2f, 1.6f, -3.2f, -6.4f, -8.0f, -8.8f, -8.8f]),
        ["Full Bass & Treble"] = new("Full Bass & Treble", 0.0f, [5.6f, 4.0f, 0.0f, -5.6f, -3.2f, 1.6f, 6.4f, 8.8f, 9.6f, 9.6f]),
        ["Full Treble"] = new("Full Treble", 0.0f, [-7.2f, -7.2f, -7.2f, -3.2f, 2.4f, 8.8f, 12.0f, 12.0f, 12.0f, 12.8f]),
        ["Headphones"] = new("Headphones", 0.0f, [4.0f, 8.0f, 4.0f, -2.4f, -1.6f, 1.6f, 4.0f, 7.2f, 9.6f, 10.4f]),
        ["Large Hall"] = new("Large Hall", 0.0f, [8.0f, 8.0f, 4.8f, 4.8f, 0.0f, -4.0f, -4.0f, -4.0f, 0.0f, 0.0f]),
        ["Live"] = new("Live", 0.0f, [-4.0f, 0.0f, 3.2f, 4.0f, 4.8f, 4.8f, 3.2f, 2.4f, 2.4f, 2.4f]),
        ["Party"] = new("Party", 0.0f, [5.6f, 5.6f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 5.6f, 5.6f]),
        ["Pop"] = new("Pop", 0.0f, [-1.6f, 1.6f, 5.6f, 6.4f, 4.8f, 0.0f, -2.4f, -2.4f, -1.6f, -1.6f]),
        ["Reggae"] = new("Reggae", 0.0f, [0.0f, 0.0f, -0.8f, -4.8f, 0.0f, 5.6f, 5.6f, 0.0f, 0.0f, 0.0f]),
        ["Rock"] = new("Rock", 0.0f, [6.4f, 4.0f, -4.8f, -6.4f, -2.4f, 3.2f, 6.4f, 8.8f, 8.8f, 8.8f]),
        ["Ska"] = new("Ska", 0.0f, [-2.4f, -4.0f, -3.2f, -0.8f, 3.2f, 4.8f, 7.2f, 8.0f, 8.8f, 8.0f]),
        ["Soft"] = new("Soft", 0.0f, [4.0f, 1.6f, 0.0f, -1.6f, 0.0f, 3.2f, 6.4f, 8.0f, 8.8f, 9.6f]),
        ["Soft Rock"] = new("Soft Rock", 0.0f, [3.2f, 3.2f, 1.6f, 0.0f, -3.2f, -4.0f, -2.4f, 0.0f, 2.4f, 6.4f]),
        ["Techno"] = new("Techno", 0.0f, [6.4f, 4.8f, 0.0f, -4.8f, -4.0f, 0.0f, 6.4f, 7.2f, 7.2f, 6.4f]),
        ["Vocal"] = new("Vocal", 0.0f, [-1.6f, -3.2f, -3.2f, 4.8f, 7.2f, 7.2f, 4.8f, 2.4f, 0.0f, -2.4f])
    };

    /// <summary>List of all preset names available.</summary>
    public static readonly IReadOnlyList<string> PresetNames = [.. PresetsByName.Keys];

    /// <summary>Retrieves a preset by name (case-insensitive) or null if not found.</summary>
    public static EqualizerPreset? TryGetPreset(string name) =>
        PresetsByName.TryGetValue(name, out var preset) ? preset : null;

    /// <summary>Retrieves a preset by name, or returns the "Flat" default if not found.</summary>
    public static EqualizerPreset GetPresetOrDefault(string name) =>
        TryGetPreset(name) ?? PresetsByName["Flat"];
}
