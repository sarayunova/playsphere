using System.Collections.Generic;

namespace PlaySphere.Domain.AI;

/// <summary>
/// Outcome of an ask-the-media run (AI-08, PICKLE.md §41): the transcript is read from the library
/// and the provider answers a natural-language question with clickable timestamp evidence. Always
/// produced, never thrown for user-correctable failures (untrusted input, PICKLE.md §43). When
/// <see cref="Success"/> is true, <see cref="Answers"/> holds the key points (each may carry an
/// optional <see cref="AiKeyPoint.StartSeconds"/> timestamp). Callers must not claim timestamp
/// support for an answer unless <see cref="AiKeyPoint.StartSeconds"/> is present.
/// </summary>
public sealed record AskMediaResult(
    bool Success,
    string? ErrorMessage,
    IReadOnlyList<AiKeyPoint>? Answers)
{
    public static AskMediaResult Fail(string errorMessage) =>
        new(false, errorMessage, null);
}
