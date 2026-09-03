using PlaySphere.Domain.Media;

namespace PlaySphere.Application.Media;

/// <summary>
/// Generates a timeline waveform (peak strip) for a local media file (PICKLE.md §20 "waveform").
/// Implementations must be bounded (subprocess timeout, capped output) and return a
/// <see cref="WaveformResult"/> — never throw — while a media file is untrusted input (§43).
/// The UI depends only on this contract, never on ffmpeg directly.
/// </summary>
public interface IWaveformProvider
{
    /// <summary>
    /// Produces (or returns from cache) a single peak-level PNG strip spanning the whole duration of
    /// <paramref name="filePath"/>, stored under <paramref name="cacheDirectory"/>. Returns a cached
    /// result without re-running ffmpeg when the file's identity inputs (path + mtime + length) match
    /// an existing strip. Failure (missing file, no audio stream, missing tool, ffmpeg error, timeout)
    /// surfaces as <see cref="WaveformResult.Success"/> == false.
    /// </summary>
    Task<WaveformResult> GenerateAsync(string filePath, string cacheDirectory, CancellationToken cancellationToken = default);
}