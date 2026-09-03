using PlaySphere.Domain.Media;

namespace PlaySphere.Application.Media;

/// <summary>
/// Converts a local media file to another container/codec via a bounded ffmpeg subprocess
/// (PICKLE.md §29 — remux, video transcode, video-to-audio, audio conversion). Media files are
/// untrusted (PICKLE.md §43), so implementations must use structured argument arrays, redirect and
/// cap subprocess output, enforce a timeout and never throw for user-correctable failures.
/// </summary>
public interface IMediaConverter
{
    /// <summary>
    /// Converts <paramref name="inputPath"/> to <paramref name="outputPath"/> using the codec/mapping
    /// described by <paramref name="preset"/>. Best-effort: returns a
    /// <see cref="ConversionResult.Fail"/> result for user-correctable failures, and only rethrows
    /// <see cref="OperationCanceledException"/> when the caller's own token is cancelled.
    /// </summary>
    Task<ConversionResult> ConvertAsync(
        string inputPath,
        string outputPath,
        ConversionPreset preset,
        CancellationToken cancellationToken = default,
        IProgress<TimeSpan>? progress = null);
}