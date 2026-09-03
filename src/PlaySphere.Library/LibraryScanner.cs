using Microsoft.Extensions.Logging;
using PlaySphere.Application.Library;
using PlaySphere.Application.Media;
using PlaySphere.Domain.Library;
using PlaySphere.Domain.Media;

namespace PlaySphere.Library;

/// <summary>
/// Recursively discovers media files under a library folder and catalogs them
/// (PICKLE.md §22 "scanning"). Each file is fingerprinted (PICKLE.md §24), probed for technical
/// metadata (duration, codec, resolution — PICKLE.md §23) via <see cref="IMediaProbe"/> when one
/// is available, and upserted. A non-probeable file is still cataloged (just without technical
/// metadata); a probe failure never aborts the scan.
/// </summary>
public sealed class LibraryScanner(
    IMediaLibraryRepository repository,
    ILogger<LibraryScanner> logger,
    IMediaProbe? probe = null) : ILibraryScanner
{
    /// <summary>Extensions recognized as media (PICKLE.md §14 container/audio targets).</summary>
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mkv", ".avi", ".mov", ".mpeg", ".mpg", ".ts", ".m2ts", ".webm", ".flv", ".ogv", ".3gp",
        ".mp3", ".aac", ".m4a", ".flac", ".wav", ".ogg", ".opus", ".alac", ".wma",
    };

    public async Task<LibraryScanResult> ScanFolderAsync(LibraryFolder folder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folder);

        IReadOnlyList<string> files;
        try
        {
            files = Directory.EnumerateFiles(folder.Path, "*", SearchOption.AllDirectories)
                .Where(f => MediaExtensions.Contains(Path.GetExtension(f)))
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            logger.LogWarning(ex, "Could not enumerate library folder '{Path}'.", folder.Path);
            return new LibraryScanResult(0, 0, 0);
        }

        var added = 0;
        var updated = 0;
        var failed = 0;

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var fingerprint = MediaFileFingerprinter.Compute(file);
                var existing = await repository.FindByFingerprintAsync(fingerprint, cancellationToken).ConfigureAwait(false);

                var item = new LibraryMediaItem
                {
                    FilePath = file,
                    Fingerprint = fingerprint,
                    Title = Path.GetFileNameWithoutExtension(file),
                    LibraryFolderId = folder.Id,
                };

                if (probe is not null)
                {
                    await ApplyProbeResultAsync(item, file, cancellationToken).ConfigureAwait(false);
                }

                await repository.UpsertMediaItemAsync(item, cancellationToken).ConfigureAwait(false);

                if (existing is null) added++; else updated++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed++;
                logger.LogWarning(ex, "Could not fingerprint media file '{Path}'.", file);
            }
        }

        logger.LogInformation("Scanned '{Path}': {Added} added, {Updated} updated, {Failed} failed.",
            folder.Path, added, updated, failed);

        return new LibraryScanResult(added, updated, failed);
    }

    /// <summary>
    /// Populates technical metadata from <see cref="IMediaProbe"/>. A file that cannot be probed
    /// (missing tool, malformed input, transient error) is left without metadata but still
    /// cataloged — the scan is not counted as a fingerprint failure. Only real cancellation
    /// propagates.
    /// </summary>
    private async Task ApplyProbeResultAsync(LibraryMediaItem item, string file, CancellationToken cancellationToken)
    {
        MediaProbeResult? result;
        try
        {
            result = await probe!.ProbeAsync(file, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not probe media file '{Path}'.", file);
            return;
        }

        if (result is null)
        {
            return;
        }

        item.Duration = result.Duration;
        item.Resolution = result.Resolution;
        item.Codec = result.Codec;
        item.FrameRate = result.FrameRate;
        item.BitrateKbps = result.BitrateKbps;
        item.AudioTrackCount = result.AudioTrackCount;
        item.SubtitleTrackCount = result.SubtitleTrackCount;
    }
}
