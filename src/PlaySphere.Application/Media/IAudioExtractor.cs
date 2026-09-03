using PlaySphere.Domain.Media;

namespace PlaySphere.Application.Media;

/// <summary>
/// Extracts the audio of a local media file into a standalone audio file suitable for AI
/// transcription (AI-02). PICKLE.md §38 prefers audio over full video for transcription, both to
/// keep the upload small and to avoid sending moving pictures. Implementations are bounded ffmpeg
/// subprocesses (PICKLE.md §43: structured args only, capped output, timeout, never throw for
/// user-correctable failures). A media file is untrusted — always fail softly, never throw.
/// </summary>
public interface IAudioExtractor
{
    /// <summary>
    /// Extracts the audio of <paramref name="inputPath"/> into <paramref name="outputPath"/> as a
    /// Whisper-compatible mono 16 kHz PCM WAV. Best-effort: returns a
    /// <see cref="AudioExtractionResult.Fail"/> result for missing input/missing tool/subprocess
    /// errors, and only rethrows <see cref="OperationCanceledException"/> when the caller's own
    /// token is cancelled.
    /// </summary>
    Task<AudioExtractionResult> ExtractAudioAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default);
}
