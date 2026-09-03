using Microsoft.Extensions.Logging;
using PlaySphere.Application.Library;
using PlaySphere.Domain.Library;

namespace PlaySphere.Library;

/// <summary>
/// Imports M3U/M3U8 playlist files into the library (PICKLE.md §25). The playlist file is untrusted
/// input (§43): content is parsed defensively, only entries that exist on disk with a supported media
/// extension are cataloged (fingerprinted + upserted), and the resulting cataloged items are appended
/// to a new playlist in file order. Missing/unsupported/unreadable entries are skipped, never fatal.
/// </summary>
public sealed class M3uImporter(
    IMediaLibraryRepository repository,
    ILogger<M3uImporter> logger) : IM3uImporter
{
    /// <summary>Reject playlist files larger than this — bounded, untrusted input (§43).</summary>
    private const long MaxPlaylistBytes = 5 * 1024 * 1024;

    public async Task<M3uImportResult> ImportAsync(string m3uFilePath, string? playlistName = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(m3uFilePath);

        string content;
        try
        {
            var info = new FileInfo(m3uFilePath);
            if (!info.Exists || info.Length <= 0 || info.Length > MaxPlaylistBytes)
            {
                throw new InvalidOperationException("Playlist file is missing, empty or too large.");
            }
            content = await File.ReadAllTextAsync(m3uFilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            logger.LogWarning(ex, "Could not read M3U playlist '{Path}'.", m3uFilePath);
            throw;
        }

        var entries = M3uParser.Parse(content, m3uFilePath);

        var name = string.IsNullOrWhiteSpace(playlistName)
            ? Path.GetFileNameWithoutExtension(m3uFilePath)
            : playlistName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            name = "Imported Playlist";
        }

        var playlist = await repository.CreatePlaylistAsync(name, cancellationToken).ConfigureAwait(false);

        var importedIds = new List<int>();
        var skipped = 0;
        var folderCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!MediaFileExtensions.IsSupportedMediaExtension(entry.Path) || !File.Exists(entry.Path))
            {
                skipped++;
                continue;
            }

            try
            {
                var fingerprint = MediaFileFingerprinter.Compute(entry.Path);
                var parentDirectory = Path.GetDirectoryName(entry.Path)
                    ?? throw new InvalidOperationException("Imported entry has no parent directory.");
                if (!folderCache.TryGetValue(parentDirectory, out var folderId))
                {
                    folderId = (await repository.EnsureFolderAsync(parentDirectory, cancellationToken).ConfigureAwait(false)).Id;
                    folderCache[parentDirectory] = folderId;
                }

                var item = new LibraryMediaItem
                {
                    FilePath = entry.Path,
                    Fingerprint = fingerprint,
                    LibraryFolderId = folderId,
                    Title = string.IsNullOrWhiteSpace(entry.Title)
                        ? Path.GetFileNameWithoutExtension(entry.Path)
                        : entry.Title,
                };
                var cataloged = await repository.UpsertMediaItemAsync(item, cancellationToken).ConfigureAwait(false);
                importedIds.Add(cataloged.Id);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                skipped++;
                continue;
            }
        }

        if (importedIds.Count > 0)
        {
            await repository.AddRangeToPlaylistAsync(playlist.Id, importedIds, cancellationToken).ConfigureAwait(false);
        }

        return new M3uImportResult(
            playlist,
            Imported: importedIds.Count,
            Skipped: skipped);
    }
}
