namespace PlaySphere.Media.Export;

/// <summary>Limits for the bounded ffmpeg waveform subprocess (untrusted input, PICKLE.md §43).</summary>
public sealed class WaveformOptions
{
    /// <summary>Path to ffmpeg.exe; when null the provider looks beside the app, else it fails softly.</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>Maximum wall-clock time for a single waveform generation.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Hard cap on captured stderr bytes, so a chatty ffmpeg can never balloon memory.</summary>
    public int MaxErrorBytes { get; set; } = 64 * 1024;

    /// <summary>Horizontal resolution of the waveform strip. Bounded to 64–2048 elsewhere.</summary>
    public int Width { get; set; } = 1080;

    /// <summary>Vertical resolution of the waveform strip. Bounded to 16–256 elsewhere.</summary>
    public int Height { get; set; } = 56;
}