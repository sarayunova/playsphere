using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PlaySphere.Domain.AI;

namespace PlaySphere.Application.AI;

/// <summary>
/// The single boundary through which every AI feature reaches the OpenAI provider (PICKLE.md §39:
/// "All OpenAI access lives in PlaySphere.AI. No UI ViewModel can instantiate OpenAI client
/// directly. Define IAiMediaService."). UI and ViewModels depend ONLY on this interface — never on a
/// concrete OpenAI client. Implementations must be strictly opt-in: no content is ever uploaded or
/// sent unless an explicit user action invokes one of these methods, and every call is cancellable.
///
/// All returned AI content is untrusted media-derived input (PICKLE.md §43) and must be treated
/// defensively by callers.
/// </summary>
public interface IAiMediaService
{
    /// <summary>
    /// Transcribes a local audio file into timed segments. <paramref name="audioFilePath"/> must be a
    /// local, existing file; callers must have already obtained the user's explicit consent (PICKLE.md
    /// §40: no silent upload, and audio is preferred over full video).
    /// </summary>
    Task<Transcript> TranscribeAudioAsync(string audioFilePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates chapter titles from a provided transcript. Never fabricates timestamps beyond those
    /// present in <paramref name="transcript"/>.
    /// </summary>
    Task<IReadOnlyList<AiChapter>> GenerateChaptersAsync(Transcript transcript, CancellationToken cancellationToken = default);

    /// <summary>
    /// Produces a concise summary of the provided transcript.
    /// </summary>
    Task<AiSummary> SummarizeAsync(Transcript transcript, CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers a natural-language question against the provided transcript, returning clickable
    /// timestamp evidence (PICKLE.md §41). Callers must not claim support for a claim unless a
    /// <see cref="AiKeyPoint.StartSeconds"/> is present.
    /// </summary>
    Task<IReadOnlyList<AiKeyPoint>> AskAsync(Transcript transcript, string question, CancellationToken cancellationToken = default);

    /// <summary>
    /// Translates every segment's text into <paramref name="targetLanguage"/>, returning segments with
    /// the exact original <see cref="TranscriptSegment.StartSeconds"/>/<see cref="TranscriptSegment.EndSeconds"/>
    /// (AI-09, PICKLE.md §38 "subtitle translation"). Never invents or shifts timestamps.
    /// </summary>
    Task<IReadOnlyList<TranscriptSegment>> TranslateAsync(Transcript transcript, string targetLanguage, CancellationToken cancellationToken = default);
}
