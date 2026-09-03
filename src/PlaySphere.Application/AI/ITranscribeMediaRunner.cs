using System.Threading;
using System.Threading.Tasks;
using PlaySphere.Domain.AI;

namespace PlaySphere.Application.AI;

/// <summary>
/// Orchestrates an end-to-end transcription for a cataloged local media file (AI-04): extract the
/// audio (AI-02, audio preferred over full video per PICKLE.md §38), send it to the provider via
/// <see cref="IAiMediaService.TranscribeAudioAsync"/>, and persist the result (PICKLE.md §42). UI
/// and ViewModels depend on this single seam — they never chain the extractor/service/repository
/// themselves and never touch ffmpeg or the provider directly.
///
/// Strictly opt-in: no audio is extracted or uploaded without an explicit user action, and the
/// caller is responsible for having already obtained consent (PICKLE.md §40). Best-effort: returns
/// a <see cref="TranscribeMediaResult.Fail"/> result for user-correctable failures (extraction
/// failure, missing file, provider error, media not found in the library) and only rethrows
/// <see cref="OperationCanceledException"/> when the caller's own token is cancelled.
/// </summary>
public interface ITranscribeMediaRunner
{
    /// <summary>
    /// Runs the full transcription pipeline for <paramref name="mediaFilePath"/>. The audio is
    /// extracted to a throwaway temp file (never persists) and deleted after the provider returns,
    /// success or failure.
    /// </summary>
    Task<TranscribeMediaResult> RunAsync(string mediaFilePath, CancellationToken cancellationToken = default);
}
