namespace PlaySphere.Domain.AI;

/// <summary>
/// Outcome of a media-summary run (AI-07, PICKLE.md §38 "media summary"): the transcript is read
/// from the library and the provider produces a concise summary. Always produced, never thrown for
/// user-correctable failures (untrusted input, PICKLE.md §43). When <see cref="Success"/> is true,
/// <see cref="Summary"/> holds the generated summary, which is also persisted as an "summary"
/// artifact for the cataloged item.
/// </summary>
public sealed record SummarizeMediaResult(
    bool Success,
    string? ErrorMessage,
    AiSummary? Summary)
{
    public static SummarizeMediaResult Fail(string errorMessage) =>
        new(false, errorMessage, null);
}
