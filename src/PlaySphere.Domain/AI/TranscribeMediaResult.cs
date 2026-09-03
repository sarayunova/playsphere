namespace PlaySphere.Domain.AI;

/// <summary>
/// Outcome of a transcribe-media run (AI-04, PICKLE.md §38/§40): extraction → provider
/// transcription → persistence. Always produced, never thrown for user-correctable failures
/// (untrusted input, PICKLE.md §43). When <see cref="Success"/> is true,
/// <see cref="Transcript"/> is the in-memory transcript for immediate display and
/// <see cref="SegmentCount"/> is the number of timed segments produced (0 with Success=true
/// means a flat text transcription without per-segment timing). Persistence is a side effect;
/// a page can reload the saved transcript from the repository by media item.
/// </summary>
public sealed record TranscribeMediaResult(
    bool Success,
    string? ErrorMessage,
    Transcript? Transcript,
    int SegmentCount)
{
    public static TranscribeMediaResult Fail(string errorMessage) =>
        new(false, errorMessage, null, 0);
}
