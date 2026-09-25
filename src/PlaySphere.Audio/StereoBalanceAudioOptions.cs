namespace PlaySphere.Audio;

/// <summary>
/// Configuration for the bounded ffmpeg subprocess that decodes the stereo-balance secondary
/// audio path (ADR-0011 Stage 2). Mirrors <c>FfMpegAudioExtractOptions</c>'s shape.
/// </summary>
public sealed class StereoBalanceAudioOptions
{
    /// <summary>Path to ffmpeg.exe; when null the session looks beside the app, else it fails softly.</summary>
    public string? ExecutablePath { get; set; }
}
