namespace PlaySphere.Media.Export;

/// <summary>Limits for the bounded ffmpeg conversion subprocess (untrusted input, PICKLE.md §43).</summary>
public sealed class FfMpegConvertOptions
{
    /// <summary>Path to ffmpeg.exe; when null the converter looks beside the app, else it fails softly.</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>Maximum wall-clock time for a single conversion. Transcodes of long files are slow, so the default is generous.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>Hard cap on captured stderr bytes, so a chatty ffmpeg can never balloon memory.</summary>
    public int MaxErrorBytes { get; set; } = 64 * 1024;
}