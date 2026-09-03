namespace PlaySphere.Playback.LibVLC;

/// <summary>
/// Configuration for libVLC engine instances. Kept intentionally small; playback
/// options are added per feature ticket (PS-103..PS-109).
/// </summary>
public sealed class LibVlcOptions
{
    /// <summary>
    /// When true, audio output is disabled ("--no-audio"). Used by headless tests/CI so
    /// tests are not coupled to a physical (or missing) audio device.
    /// </summary>
    public bool DisableAudio { get; set; }

    /// <summary>libVLC verbosity, clamped to 0–2.</summary>
    public int Verbosity { get; set; } = 1;

    /// <summary>Extra raw libVLC arguments appended to the default set.</summary>
    public string[] ExtraArguments { get; set; } = [];

    /// <summary>
    /// When set, the engine forwards libVLC's native message log (at the current
    /// <see cref="Verbosity"/>) to this file, one message per line, so real playback
    /// failures (e.g. "no suitable audio output module") can be diagnosed headlessly.
    /// Leave null to skip file logging. Test fixtures keep this null so they never
    /// write files.
    /// </summary>
    public string? LogFilePath { get; set; }
}