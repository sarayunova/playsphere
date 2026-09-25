using System.Threading;
using System.Threading.Tasks;
using PlaySphere.Domain.AI;

namespace PlaySphere.Application.AI;

/// <summary>
/// The opt-in orchestration seam the UI depends on for AI-09 subtitle translation (PICKLE.md
/// §38/§40/§42): loads the persisted transcript for a cataloged media file, asks the
/// <see cref="IAiMediaService"/> to translate each segment's text into the requested language while
/// preserving the original timestamps, and persists the result as a per-language artifact for the
/// item. Implementations must be strictly opt-in (no provider call unless invoked for an explicit user
/// action) and fail softly (return a <see cref="TranslateMediaResult"/> with
/// <see cref="TranslateMediaResult.Success"/> false rather than throw, for user-correctable failures).
/// All produced content is untrusted (PICKLE.md §43).
/// </summary>
public interface ITranslateMediaRunner
{
    /// <summary>The persistent-artifact key prefix a translation is saved under.</summary>
    const string ArtifactKeyPrefix = "translation:";

    /// <summary>
    /// The persisted-artifact key for a translation into <paramref name="targetLanguage"/> (normalized
    /// to lowercase/trimmed so the same language always round-trips to the same key).
    /// </summary>
    static string ArtifactKeyFor(string targetLanguage) =>
        ArtifactKeyPrefix + (targetLanguage ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>
    /// Translates the cataloged media at <paramref name="mediaFilePath"/> into
    /// <paramref name="targetLanguage"/> from its persisted transcript.
    /// </summary>
    Task<TranslateMediaResult> TranslateAsync(string mediaFilePath, string targetLanguage, CancellationToken cancellationToken = default);
}
