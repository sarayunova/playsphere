using System.Threading;
using System.Threading.Tasks;
using PlaySphere.Domain.AI;

namespace PlaySphere.Application.AI;

/// <summary>
/// The opt-in orchestration seam the UI depends on for AI-08 "ask the media" (PICKLE.md §41/§40/§43):
/// loads the persisted transcript for a cataloged media file and asks the <see cref="IAiMediaService"/>
/// a natural-language question, returning clickable timestamp evidence. Implementations must be strictly
/// opt-in and fail softly (return an <see cref="AskMediaResult"/> with <see cref="AskMediaResult.Success"/>
/// false rather than throw, for user-correctable failures). All produced content is untrusted (PICKLE.md §43).
/// </summary>
public interface IAskMediaRunner
{
    /// <summary>Asks <paramref name="question"/> against the cataloged media at <paramref name="mediaFilePath"/>.</summary>
    Task<AskMediaResult> AskAsync(string mediaFilePath, string question, CancellationToken cancellationToken = default);
}
