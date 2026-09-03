namespace PlaySphere.Library;

/// <summary>
/// Shared filter for media file extensions (PICKLE.md §14 container/audio targets). Mirrors the
/// scanner's private set; used by the M3U importer to decide whether an imported entry is a
/// catalogable media file.
/// </summary>
public static class MediaFileExtensions
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mkv", ".avi", ".mov", ".mpeg", ".mpg", ".ts", ".m2ts", ".webm", ".flv", ".ogv", ".3gp",
        ".mp3", ".aac", ".m4a", ".flac", ".wav", ".ogg", ".opus", ".alac", ".wma",
    };

    /// <summary>True when the file's extension is a recognized media container/audio format.</summary>
    public static bool IsSupportedMediaExtension(string filePath)
        => !string.IsNullOrWhiteSpace(filePath) && Supported.Contains(Path.GetExtension(filePath));
}
