using System.Collections.Generic;

namespace PlaySphere.Domain.AI;

/// <summary>
/// Outcome of a subtitle-translation run (AI-09, PICKLE.md §38 "subtitle translation"): the transcript
/// is read from the library and the provider re-renders each segment's text in the requested language,
/// preserving the original timestamps verbatim. Always produced, never thrown for user-correctable
/// failures (untrusted input, PICKLE.md §43). When <see cref="Success"/> is true, <see cref="Segments"/>
/// holds the translated, timed segments, which are also persisted as a per-language artifact for the
/// cataloged item (see <see cref="Application.AI.ITranslateMediaRunner.ArtifactKeyFor"/>).
/// </summary>
public sealed record TranslateMediaResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<TranscriptSegment>? Segments,
    string? TargetLanguage)
{
    public static TranslateMediaResult Fail(string errorMessage) =>
        new(false, errorMessage, null, null);
}
