namespace PlaySphere.Domain.Media;

/// <summary>
/// Outcome of a timeline waveform generation (PICKLE.md §20 "waveform"). The waveform is a single
/// peak-level PNG strip covering the entire media duration; the UI stretches it along the scrub bar
/// as a quiet visual aid. Always produced, never thrown.
/// </summary>
public sealed record WaveformResult(
    bool Success,
    string? ImagePath,
    int Width,
    int Height,
    string? ErrorMessage)
{
    public static WaveformResult Ok(string imagePath, int width, int height) =>
        new(true, imagePath, width, height, null);

    public static WaveformResult Fail(string errorMessage) =>
        new(false, null, 0, 0, errorMessage);
}