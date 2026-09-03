using PlaySphere.Domain.Media;

namespace PlaySphere.Application.Media;

/// <summary>
/// Trims a contiguous segment from a local media file into a new file (PICKLE.md §1102 "clip
/// export"). Implemented by the bounded ffmpeg subprocess in the <c>PlaySphere.Media.Export</c>
/// module. UI depends on this contract, never on the concrete subprocess tool. Runs must be
/// bounded (timeout, cancellation) and input must be treated as untrusted: a failed/malformed
/// export returns a <see cref="ClipExportResult.Fail"/> result rather than throwing.
/// </summary>
public interface IMediaClipExporter
{
    /// <summary>
    /// Exports the segment starting at <paramref name="start"/> and lasting
    /// <paramref name="duration"/> from <paramref name="inputPath"/> to <paramref name="outputPath"/>.
    /// Cancellation is honored; a timeout or cancelled run reports a failure result (it never throws
    /// <see cref="OperationCanceledException"/> for a *caller-initiated* cancel — the caller observes
    /// a failure result).
    /// </summary>
    Task<ClipExportResult> ExportClipAsync(
        string inputPath,
        string outputPath,
        TimeSpan start,
        TimeSpan duration,
        CancellationToken cancellationToken = default,
        IProgress<TimeSpan>? progress = null);
}