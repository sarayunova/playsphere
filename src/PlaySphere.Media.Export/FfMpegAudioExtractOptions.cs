namespace PlaySphere.Media.Export;

/// <summary>
/// Limits for the bounded ffmpeg audio-extraction subprocess (AI-02; untrusted input, PICKLE.md §43).
/// </summary>
public sealed class FfMpegAudioExtractOptions
{
    /// <summary>Path to ffmpeg.exe; when null the extractor looks beside the app, else it fails softly.</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>Maximum wall-clock time for a single extraction.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Hard cap on captured stderr bytes, so a chatty ffmpeg can never balloon memory.</summary>
    public int MaxErrorBytes { get; set; } = 64 * 1024;
}
