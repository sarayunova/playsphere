using PlaySphere.Domain.Library;

namespace PlaySphere.Application.Library;

/// <summary>
/// Discovers media files under a library folder and catalogs them via
/// <see cref="IMediaLibraryRepository"/> (PICKLE.md §22 "scanning"). UI depends on this contract,
/// never on the concrete scanner (PlaySphere.Library) directly.
/// </summary>
public interface ILibraryScanner
{
    /// <summary>
    /// Recursively scans <paramref name="folder"/>'s directory for recognized media files,
    /// fingerprints each one (PICKLE.md §24) and upserts it into the catalog. Files that can't
    /// be read (locked, permission denied) are skipped and counted as failed rather than
    /// aborting the whole scan.
    /// </summary>
    Task<LibraryScanResult> ScanFolderAsync(LibraryFolder folder, CancellationToken cancellationToken = default);
}
