namespace PlaySphere.Application.Library;

/// <summary>
/// Exports media file paths as an M3U playlist file (PICKLE.md §25). Writes a standard
/// <c>#EXTM3U</c> header with one <c>#EXTINF</c> + path line per entry.
/// </summary>
public interface IM3uExporter
{
    /// <summary>Writes <paramref name="mediaPaths"/> to <paramref name="destinationFilePath"/> as M3U. Returns false on failure.</summary>
    Task<bool> ExportAsync(IReadOnlyList<string> mediaPaths, string destinationFilePath, CancellationToken cancellationToken = default);
}
