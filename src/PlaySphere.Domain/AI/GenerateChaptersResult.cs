using System.Collections.Generic;
using PlaySphere.Domain.Library;

namespace PlaySphere.Domain.AI;

/// <summary>
/// Outcome of an auto-chapters run (AI-06, PICKLE.md §38 "automatic chapters"): the AI-generated
/// chapter list, persisted for the cataloged item, for immediate display. Always produced, never
/// thrown for user-correctable failures (untrusted input, PICKLE.md §43). When
/// <see cref="Success"/> is true, <see cref="Chapters"/> holds the persisted rows (ordered by start).
/// </summary>
public sealed record GenerateChaptersResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<AiChapterEntity>? Chapters)
{
    public static GenerateChaptersResult Fail(string errorMessage) =>
        new(false, errorMessage, null);
}
