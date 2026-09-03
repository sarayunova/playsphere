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
/// Default <see cref="IAskMediaRunner"/>. Chains the library repository's persisted-transcript load
/// and the <see cref="IAiMediaService.AskAsync"/> boundary into one opt-in, best-effort step (see the
/// interface doc and PICKLE.md §41/§40/§43). Ask results are a live query and are not persisted.
/// </summary>
public sealed class AskMediaRunner : IAskMediaRunner
{
    private readonly IAiMediaService _ai;
    private readonly IMediaLibraryRepository _repository;
    private readonly ILogger<AskMediaRunner>? _logger;

    public AskMediaRunner(
        IAiMediaService ai,
        IMediaLibraryRepository repository,
        ILogger<AskMediaRunner>? logger = null)
    {
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger;
    }

    public async Task<AskMediaResult> AskAsync(string mediaFilePath, string question, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(question))
        {
            return AskMediaResult.Fail("Enter a question first.");
        }

        if (string.IsNullOrWhiteSpace(mediaFilePath) || !File.Exists(mediaFilePath))
        {
            return AskMediaResult.Fail("The media file does not exist or could not be read.");
        }

        var item = await FindCatalogedItemAsync(mediaFilePath, cancellationToken).ConfigureAwait(false);
        if (item is null)
        {
            return AskMediaResult.Fail(
                "This file is not in your media library. Add it first (Library → Add Folder, or scan) before asking.");
        }

        var transcript = await LoadTranscriptAsync(item.Id, cancellationToken).ConfigureAwait(false);
        if (transcript is null)
        {
            return AskMediaResult.Fail(
                "There is no transcript for this file yet. Transcribe it first (AI Media → Transcribe) before asking.");
        }

        var domainTranscript = BuildDomainTranscript(transcript);
        if (domainTranscript.Segments.Count == 0)
        {
            return AskMediaResult.Fail("The saved transcript is empty; transcribe it again before asking.");
        }

        IReadOnlyList<AiKeyPoint> answers;
        try
        {
            answers = await _ai.AskAsync(domainTranscript, question, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Ask call failed");
            return AskMediaResult.Fail(
                ex is AiException aiEx ? aiEx.Message : "The AI provider could not answer. Try again.");
        }

        if (answers is null || answers.Count == 0)
        {
            return AskMediaResult.Fail("The provider returned no answers. Try rephrasing the question.");
        }

        return new AskMediaResult(Success: true, ErrorMessage: null, Answers: answers);
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
            _logger?.LogWarning(ex, "Library lookup failed during ask");
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
            _logger?.LogWarning(ex, "Transcript load failed during ask");
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
}
