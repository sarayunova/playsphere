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
/// Default <see cref="ISummarizeMediaRunner"/>. Chains the library repository's persisted-transcript
/// load, the <see cref="IAiMediaService.SummarizeAsync"/> boundary and the repository's artifact
/// persistence into one opt-in, best-effort step (see the interface doc and PICKLE.md §38/§40/§42).
/// </summary>
public sealed class SummarizeMediaRunner : ISummarizeMediaRunner
{
    private readonly IAiMediaService _ai;
    private readonly IMediaLibraryRepository _repository;
    private readonly ILogger<SummarizeMediaRunner>? _logger;

    public SummarizeMediaRunner(
        IAiMediaService ai,
        IMediaLibraryRepository repository,
        ILogger<SummarizeMediaRunner>? logger = null)
    {
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger;
    }

    public async Task<SummarizeMediaResult> SummarizeAsync(string mediaFilePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mediaFilePath) || !File.Exists(mediaFilePath))
        {
            return SummarizeMediaResult.Fail("The media file does not exist or could not be read.");
        }

        var item = await FindCatalogedItemAsync(mediaFilePath, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return SummarizeMediaResult.Fail(
                "This file is not in your media library. Add it first (Library → Add Folder, or scan) before summarizing.");
        }

        var transcript = await LoadTranscriptAsync(item.Id, cancellationToken).ConfigureAwait(false);
        if (transcript is null)
        {
            return SummarizeMediaResult.Fail(
                "There is no transcript for this file yet. Transcribe it first (AI Media → Transcribe) before summarizing.");
        }

        var domainTranscript = BuildDomainTranscript(transcript);
        if (domainTranscript.Segments.Count == 0)
        {
            return SummarizeMediaResult.Fail("The saved transcript is empty; transcribe it again before summarizing.");
        }

        AiSummary summary;
        try
        {
            summary = await _ai.SummarizeAsync(domainTranscript, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Summary call failed");
            return SummarizeMediaResult.Fail(
                ex is AiException aiEx ? aiEx.Message : "The AI provider could not produce a summary. Try again.");
        }

        if (summary is null || string.IsNullOrWhiteSpace(summary.Text))
        {
            return SummarizeMediaResult.Fail("The provider returned an empty summary.");
        }

        await PersistSummaryAsync(item.Id, summary, cancellationToken).ConfigureAwait(false);

        return new SummarizeMediaResult(Success: true, ErrorMessage: null, Summary: summary);
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
            _logger?.LogWarning(ex, "Library lookup failed during summary generation");
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
            _logger?.LogWarning(ex, "Transcript load failed during summary generation");
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

    private async Task PersistSummaryAsync(int mediaItemId, AiSummary summary, CancellationToken cancellationToken)
    {
        try
        {
            var payload = System.Text.Json.JsonSerializer.Serialize(summary);
            await _repository.SaveArtifactAsync(new AiArtifact
            {
                MediaItemId = mediaItemId,
                Key = ISummarizeMediaRunner.SummaryArtifactKey,
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
            // Persistence is best-effort; the in-memory summary still displays.
            _logger?.LogWarning(ex, "Summary persistence failed");
        }
    }
}
