using PlaySphere.Domain.Media;

namespace PlaySphere.Application.Media;

/// <summary>
/// Serial background queue for conversion and clip-export jobs (PICKLE.md §30). Each job runs as a
/// bounded ffmpeg subprocess below normal priority so a heavy queue can never starve ongoing
/// playback, and exposes progress, cancel, retry and open-output-folder. UI depends on this
/// contract, never on the <c>PlaySphere.Worker</c> implementation or ffmpeg directly.
/// </summary>
public interface IConversionQueue : IDisposable
{
    /// <summary>Raised whenever a job is added, updated (progress/status) or removed.</summary>
    event EventHandler? JobsChanged;

    /// <summary>Live snapshot of all jobs, newest first. The returned list is a snapshot copy.</summary>
    IReadOnlyList<ConversionJob> Jobs { get; }

    /// <summary>
    /// Queues a full conversion. Returns <c>false</c> (never throws) when the input is missing, the
    /// output extension does not match the preset, or both paths are equal.
    /// </summary>
    bool EnqueueConvert(string inputPath, string outputPath, ConversionPreset preset);

    /// <summary>
    /// Queues a clip export. Returns <c>false</c> (never throws) when the input is missing, the
    /// output container is unsupported, the start is negative or the duration is zero.
    /// </summary>
    bool EnqueueClip(string inputPath, string outputPath, TimeSpan start, TimeSpan duration);

    /// <summary>Cancels an active (queued or running) job. No-op for finished jobs.</summary>
    void Cancel(string jobId);

    /// <summary>Re-queues a failed job for another attempt. No-op for non-failed jobs.</summary>
    void Retry(string jobId);
}