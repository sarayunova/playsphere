using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PlaySphere.Application.AI;
using PlaySphere.Application.Library;
using PlaySphere.Application.Media;
using PlaySphere.Domain.AI;
using PlaySphere.Domain.Library;

namespace PlaySphere.AI;

/// <summary>
/// Default <see cref="ITranscribeMediaRunner"/>. Chains the AI-02 audio extractor, the
/// <see cref="IAiMediaService"/> transcription boundary and the library repository's transcript
/// persistence into one opt-in, best-effort step (see the interface doc for the contract and
/// PICKLE.md §38/§40/§42).
/// </summary>
public sealed class TranscribeMediaRunner : ITranscribeMediaRunner
{
    private const string SourceModelTag = "whisper";

    private readonly IAudioExtractor _audioExtractor;
    private readonly IAiMediaService _ai;
    private readonly IMediaLibraryRepository _repository;
    private readonly ILogger<TranscribeMediaRunner>? _logger;

    public TranscribeMediaRunner(
        IAudioExtractor audioExtractor,
        IAiMediaService ai,
        IMediaLibraryRepository repository,
        ILogger<TranscribeMediaRunner>? logger = null)
    {
        _audioExtractor = audioExtractor ?? throw new ArgumentNullException(nameof(audioExtractor));
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger;
    }

    public async Task<TranscribeMediaResult> RunAsync(string mediaFilePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mediaFilePath) || !File.Exists(mediaFilePath))
        {
            return TranscribeMediaResult.Fail("The media file does not exist or could not be read.");
        }

        // Persistence is keyed off the cataloged item, so the media must be in the library.
        LibraryMediaItem? item;
        try
        {
            item = await _repository.FindByPathAsync(mediaFilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Library lookup failed during transcription setup");
            item = null;
        }

        if (item is null)
        {
            return TranscribeMediaResult.Fail(
                "This file is not in your media library. Add it first (Library → Add Folder, or scan) before transcribing.");
        }

        var workingDir = Path.Combine(Path.GetTempPath(), "PlaySphereAi", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workingDir);
        var wavPath = Path.Combine(workingDir, "audio.wav");

        try
        {
            _logger?.LogInformation("Transcribing {Path}", mediaFilePath);

            var extraction = await _audioExtractor.ExtractAudioAsync(mediaFilePath, wavPath, cancellationToken).ConfigureAwait(false);
            if (!extraction.Success)
            {
                return TranscribeMediaResult.Fail($"Could not extract the audio: {extraction.ErrorMessage}");
            }

            var (transcript, errorMessage) = await TranscribeAndPersistAsync(item.Id, wavPath, cancellationToken).ConfigureAwait(false);
            if (transcript is null)
            {
                return TranscribeMediaResult.Fail(errorMessage ?? "The AI provider did not return a usable transcript.");
            }

            return new TranscribeMediaResult(
                Success: true,
                ErrorMessage: null,
                Transcript: transcript,
                SegmentCount: transcript.Segments.Count);
        }
        finally
        {
            TryDeleteDirectory(workingDir);
        }
    }

    private async Task<(Transcript? Transcript, string? ErrorMessage)> TranscribeAndPersistAsync(
        int mediaItemId,
        string wavPath,
        CancellationToken cancellationToken)
    {
        Transcript transcript;
        try
        {
            transcript = await _ai.TranscribeAudioAsync(wavPath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Transcription call failed");
            return (null, ex is AiException aiEx ? aiEx.Message : "The AI provider call failed unexpectedly.");
        }

        if (transcript is null || transcript.IsEmpty)
        {
            return (null, "The provider returned an empty transcript.");
        }

        await PersistAsync(mediaItemId, transcript, cancellationToken).ConfigureAwait(false);
        return (transcript, null);
    }

    private async Task<AiTranscript?> PersistAsync(
        int mediaItemId,
        Transcript transcript,
        CancellationToken cancellationToken)
    {
        var entity = new AiTranscript
        {
            MediaItemId = mediaItemId,
            Language = transcript.Language,
            SourceModel = SourceModelTag,
            CreatedAt = DateTime.UtcNow,
        };

        var segments = new List<AiTranscriptSegment>();
        for (var i = 0; i < transcript.Segments.Count; i++)
        {
            var s = transcript.Segments[i];
            segments.Add(new AiTranscriptSegment
            {
                Ordinal = i,
                StartSeconds = s.StartSeconds,
                EndSeconds = s.EndSeconds,
                Text = s.Text,
            });
        }

        try
        {
            return await _repository.SaveTranscriptAsync(entity, segments, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Persistence is best-effort relative to a successful transcription; the in-memory
            // transcript still displays, but it will not survive a restart.
            _logger?.LogWarning(ex, "Transcript persistence failed");
            return null;
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup of a throwaway temp directory.
        }
    }
}
