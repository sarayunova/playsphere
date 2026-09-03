using PlaySphere.Domain.Media;

namespace PlaySphere.Application.Media;

/// <summary>
/// Reads technical metadata for a media file (PICKLE.md §23). Implemented by the FFprobe-based
/// <c>PlaySphere.Media.Probe</c> module. UI, the library scanner and other modules depend on this
/// contract, never on the concrete subprocess tool directly. Implementations must be bounded:
/// a media file is untrusted, so probing needs a timeout, cancellation and recoverable failure —
/// a non-probeable file returns <c>null</c> rather than throwing up to the caller.
/// </summary>
public interface IMediaProbe
{
    /// <summary>
    /// Probes <paramref name="filePath"/> and returns its technical metadata, or <c>null</c> if
    /// the file cannot be read or is not a probeable media file (missing file, transient I/O
    /// error, timeout, cancellation reaches the caller via <see cref="OperationCanceledException"/>).
    /// </summary>
    Task<MediaProbeResult?> ProbeAsync(string filePath, CancellationToken cancellationToken = default);
}
