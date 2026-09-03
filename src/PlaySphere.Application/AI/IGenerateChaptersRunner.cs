using System.Threading;
using System.Threading.Tasks;
using PlaySphere.Domain.AI;

namespace PlaySphere.Application.AI;

/// <summary>
/// Orchestrates AI auto-chapters (AI-06, PICKLE.md §38 "automatic chapters" / §42 chapters table) for
/// a cataloged local media file: load the persisted transcript (the chapters derive from it — there is
/// nothing to chapter without one), call <see cref="IAiMediaService.GenerateChaptersAsync"/>, and
/// persist the result. UI and ViewModels depend on this single seam — they never chain the
/// repository/service themselves and never touch the provider directly (PICKLE.md §39).
///
/// Strictly opt-in: no provider call happens unless the user explicitly invokes a generate action
/// (PICKLE.md §40). Best-effort: returns a <see cref="GenerateChaptersResult.Fail"/> result for
/// user-correctable failures (missing file, media not in the library, no transcript yet, provider
/// error) and only rethrows <see cref="OperationCanceledException"/> when the caller's own token is
/// cancelled.
/// </summary>
public interface IGenerateChaptersRunner
{
    Task<GenerateChaptersResult> GenerateChaptersAsync(string mediaFilePath, CancellationToken cancellationToken = default);
}
