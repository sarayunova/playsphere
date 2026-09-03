using PlaySphere.Domain.Library;

namespace PlaySphere.Application.Library;

/// <summary>Outcome of an M3U import.</summary>
public sealed record M3uImportResult(Playlist Playlist, int Imported, int Skipped);

/// <summary>
/// Imports an M3U/M3U8 playlist file (PICKLE.md §25). Parses entries (untrusted input, §43),
/// catalogs any referenced media file that exists on disk and has a supported media extension,
/// and appends the resulting cataloged items to a playlist in file order. Missing, unsupported or
/// unreadable entries are skipped, never fatal.
/// </summary>
public interface IM3uImporter
{
    /// <summary>Imports <paramref name="m3uFilePath"/> into a playlist of the given name (defaults to the file name).</summary>
    Task<M3uImportResult> ImportAsync(string m3uFilePath, string? playlistName = null, CancellationToken cancellationToken = default);
}
