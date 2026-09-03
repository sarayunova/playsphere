namespace PlaySphere.Domain.Media;

/// <summary>
/// Outcome of an audio-extraction run (AI-02, PICKLE.md §38: audio is preferred over full video)
/// and the path to the produced audio file. Always produced, never thrown — untrusted input
/// (PICKLE.md §43) fails softly via <see cref="Fail"/>.
/// </summary>
public sealed record AudioExtractionResult(bool Success, string? ErrorMessage, string? OutputPath)
{
    public static AudioExtractionResult Ok(string outputPath) => new(true, null, outputPath);

    public static AudioExtractionResult Fail(string errorMessage) => new(false, errorMessage, null);
}
