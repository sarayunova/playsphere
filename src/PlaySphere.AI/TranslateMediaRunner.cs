using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PlaySphere.Application.AI;
using PlaySphere.Application.Library;
using PlaySphere.Domain.AI;
using PlaySphere.Domain.Library;

namespace PlaySphere.AI;

/// <summary>
/// Default <see cref="ITranslateMediaRunner"/>. Chains the library repository's persisted-transcript
/// load, the <see cref="IAiMediaService.TranslateAsync"/> boundary and the repository's artifact
/// persistence into one opt-in, best-effort step (see the interface doc and PICKLE.md §38/§40/§42).
/// </summary>
public sealed class TranslateMediaRunner : ITranslateMediaRunner
{
    private readonly IAiMediaService _ai;
    private readonly IMediaLibraryRepository _repository;
    private readonly ILogger<TranslateMediaRunner>? _logger;

    public TranslateMediaRunner(
        IAiMediaService ai,
        IMediaLibraryRepository repository,
        ILogger<TranslateMediaRunner>? logger = null)
    {
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger;
    }

    public async Task<TranslateMediaResult> TranslateAsync(string mediaFilePath, string targetLanguage, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mediaFilePath) || !File.Exists(mediaFilePath))
        {
            return TranslateMediaResult.Fail("The media file does not exist or could not be read.");
        }

        if (string.IsNullOrWhiteSpace(targetLanguage))
        {
            return TranslateMediaResult.Fail("Choose a target language before translating.");
        }

        var item = await FindCatalogedItemAsync(mediaFilePath, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return TranslateMediaResult.Fail(
                "This file is not in your media library. Add it first (Library → Add Folder, or scan) before translating.");
        }

        var transcript = await LoadTranscriptAsync(item.Id, cancellationToken).ConfigureAwait(false);
        if (transcript is null)
        {
            return TranslateMediaResult.Fail(
                "There is no transcript for this file yet. Transcribe it first (AI Media → Transcribe) before translating.");
        }

        var domainTranscript = BuildDomainTranscript(transcript);
        if (domainTranscript.Segments.Count == 0)
        {
            return TranslateMediaResult.Fail("The saved transcript is empty; transcribe it again before translating.");
        }

        var normalizedLanguage = targetLanguage.Trim();
        IReadOnlyList<TranscriptSegment> translated;
        try
        {
            translated = await _ai.TranslateAsync(domainTranscript, normalizedLanguage, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Translation call failed");
            return TranslateMediaResult.Fail(
                ex is AiException aiEx ? aiEx.Message : "The AI provider could not translate this transcript. Try again.");
        }

        if (translated is null || translated.Count == 0)
        {
            return TranslateMediaResult.Fail("The provider returned no translated segments.");
        }

        await PersistTranslationAsync(item.Id, normalizedLanguage, translated, cancellationToken).ConfigureAwait(false);

        return new TranslateMediaResult(Success: true, ErrorMessage: null, Segments: translated, TargetLanguage: normalizedLanguage);
    }

    private async Task<LibraryMediaItem?> FindCatalogedItemAsync(string mediaFilePath, CancellationToken cancellationToken)
    {
        try
        {
            return await _repository.FindByPathAsync(mediaFilePath, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Library lookup failed during translation");
            return null;
        }
    }

    private async Task<AiTranscript?> LoadTranscriptAsync(int mediaItemId, CancellationToken cancellationToken)
    {
        try
        {
            var transcript = await _repository.GetTranscriptAsync(mediaItemId, cancellationToken).ConfigureAwait(false);
            return transcript is not null && transcript.Segments is { Count: > 0 } ? transcript : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Transcript load failed during translation");
            return null;
        }
    }

    private static Transcript BuildDomainTranscript(AiTranscript transcript)
    {
        var segments = new List<TranscriptSegment>(transcript.Segments!.Count);
        foreach (var s in transcript.Segments!)
        {
            segments.Add(new TranscriptSegment(
                StartSeconds: s.StartSeconds,
                EndSeconds: s.EndSeconds,
                Text: s.Text));
        }
        segments.Sort((a, b) => a.StartSeconds.CompareTo(b.StartSeconds));
        return new Transcript(segments, transcript.Language);
    }

    private async Task PersistTranslationAsync(
        int mediaItemId,
        string targetLanguage,
        IReadOnlyList<TranscriptSegment> segments,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(segments);
            await _repository.SaveArtifactAsync(new AiArtifact
            {
                MediaItemId = mediaItemId,
                Key = ITranslateMediaRunner.ArtifactKeyFor(targetLanguage),
                PayloadJson = payload,
                CreatedAt = DateTime.UtcNow,
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Persistence is best-effort; the in-memory translation still displays.
            _logger?.LogWarning(ex, "Translation persistence failed");
        }
    }
}
