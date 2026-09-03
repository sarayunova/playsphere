using PlaySphere.Domain.Media;

namespace PlaySphere.Application.Media;

/// <summary>
/// Generates a timeline hover-thumbnail filmstrip for a local media file (PICKLE.md §20
/// "hover thumbnail"). Implementations must be bounded (subprocess timeout, capped output) and
/// return a <see cref="ThumbnailStripResult"/> — never throw — while a media file is untrusted
/// input (§43). The UI depends only on this contract, never on ffmpeg directly.
/// </summary>
public interface IMediaThumbnailProvider
{
    /// <summary>
    /// Produces (or returns from cache) a single contact-sheet JPEG made of evenly time-spaced
    /// frames sampled from <paramref name="filePath"/> into <paramref name="cacheDirectory"/>.
    /// <paramref name="frameCount"/> is the desired number of cells (bounded by the implementation);
    /// <paramref name="duration"/> drives the sampling interval. Returns a cached result without
    /// re-running ffmpeg when the file's identity inputs (path + mtime + length) match an existing
    /// sheet. Failure (missing file, unknown duration, missing tool, ffmpeg error, timeout) surfaces
    /// as <see cref="ThumbnailStripResult.Success"/> == false.
    /// </summary>
    Task<ThumbnailStripResult> GenerateStripAsync(
        string filePath,
        string cacheDirectory,
        TimeSpan duration,
        int frameCount,
        CancellationToken cancellationToken = default);
}