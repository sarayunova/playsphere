using System.Threading;
using System.Threading.Tasks;
using PlaySphere.Domain.AI;

namespace PlaySphere.Application.AI;

/// <summary>
/// The opt-in orchestration seam the UI depends on for AI-07 media summaries (PICKLE.md §38/§40/§42):
/// loads the persisted transcript for a cataloged media file, asks the <see cref="IAiMediaService"/>
/// for a concise summary, and persists it as an "summary" artifact for the item. Implementations must
/// be strictly opt-in (no provider call unless invoked for an explicit user action) and fail softly
/// (return a <see cref="SummarizeMediaResult"/> with <see cref="SummarizeMediaResult.Success"/> false
/// rather than throw, for user-correctable failures). All produced content is untrusted (PICKLE.md §43).
/// </summary>
public interface ISummarizeMediaRunner
{
    /// <summary>The persistent-artifact key a summary is saved under for a cataloged item.</summary>
    const string SummaryArtifactKey = "summary";

    /// <summary>Summarizes the cataloged media at <paramref name="mediaFilePath"/> from its persisted transcript.</summary>
    Task<SummarizeMediaResult> SummarizeAsync(string mediaFilePath, CancellationToken cancellationToken = default);
}
