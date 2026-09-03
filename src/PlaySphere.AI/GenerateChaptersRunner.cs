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
/// Default <see cref="IGenerateChaptersRunner"/>. Chains the library repository's persisted-transcript
/// load, the <see cref="IAiMediaService.GenerateChaptersAsync"/> boundary and the repository's chapter
/// persistence into one opt-in, best-effort step (see the interface doc and PICKLE.md §38/§40/§42).
/// </summary>
public sealed class GenerateChaptersRunner : IGenerateChaptersRunner
{
    private readonly IAiMediaService _ai;
    private readonly IMediaLibraryRepository _repository;
    private readonly ILogger<GenerateChaptersRunner>? _logger;

    public GenerateChaptersRunner(
        IAiMediaService ai,
        IMediaLibraryRepository repository,
        ILogger<GenerateChaptersRunner>? logger = null)
    {
        _ai = ai ?? throw new ArgumentNullException(nameof(ai));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger;
    }

    public async Task<GenerateChaptersResult> GenerateChaptersAsync(string mediaFilePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mediaFilePath) || !File.Exists(mediaFilePath))
        {
            return GenerateChaptersResult.Fail("The media file does not exist or could not be read.");
        }

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
            _logger?.LogWarning(ex, "Library lookup failed during chapter generation");
            item = null;
        }

        if (item is null)
        {
            return GenerateChaptersResult.Fail(
                "This file is not in your media library. Add it first (Library → Add Folder, or scan) before generating chapters.");
        }

        AiTranscript? transcript;
        try
        {
            transcript = await _repository.GetTranscriptAsync(item.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Transcript load failed during chapter generation");
            transcript = null;
        }

        if (transcript is null || transcript.Segments is null || transcript.Segments.Count == 0)
        {
            return GenerateChaptersResult.Fail(
                "There is no transcript for this file yet. Transcribe it first (AI Media → Transcribe) before generating chapters.");
        }

        var domainTranscript = ToDomainTranscript(transcript);
        if (domainTranscript.Segments.Count == 0)
        {
            return GenerateChaptersResult.Fail("The saved transcript is empty; transcribe it again before generating chapters.");
        }

        IReadOnlyList<AiChapter> generated;
        try
        {
            generated = await _ai.GenerateChaptersAsync(domainTranscript, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Chapter generation call failed");
            return GenerateChaptersResult.Fail(
                ex is AiException aiEx ? aiEx.Message : "The AI provider could not generate chapters. Try again.");
        }

        if (generated is null || generated.Count == 0)
        {
            return GenerateChaptersResult.Fail("The provider returned no chapters.");
        }

        var toPersist = BuildEntities(item.Id, generated);
        if (toPersist.Count == 0)
        {
            return GenerateChaptersResult.Fail("The provider returned no usable chapters.");
        }

        try
        {
            await _repository.SaveChaptersAsync(item.Id, toPersist, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Chapter persistence failed");
            // Persistence is best-effort; still report success so the UI can choose to display
            // the in-memory chapters, since a provider round-trip already happened.
            return new GenerateChaptersResult(Success: true, ErrorMessage: null, Chapters: toPersist);
        }

        return new GenerateChaptersResult(Success: true, ErrorMessage: null, Chapters: toPersist);
    }

    private static Transcript ToDomainTranscript(AiTranscript transcript)
    {
        var segments = new List<TranscriptSegment>(transcript.Segments!.Count);
        foreach (var s in transcript.Segments!)
        {
            segments.Add(new TranscriptSegment(
                StartSeconds: s.StartSeconds,
                EndSeconds: s.EndSeconds,
                Text: s.Text));
        }
        // Persisted segments are already ordered by Ordinal, but sort defensively by start so the
        // provider always sees monotonic content regardless of storage details.
        segments.Sort((a, b) => a.StartSeconds.CompareTo(b.StartSeconds));
        return new Transcript(segments, transcript.Language);
    }

    private static List<AiChapterEntity> BuildEntities(int mediaItemId, IReadOnlyList<AiChapter> generated)
    {
        var entities = new List<AiChapterEntity>(generated.Count);
        foreach (var c in generated)
        {
            var title = c.Title?.Trim() ?? string.Empty;
            var start = c.StartSeconds;
            if (title.Length == 0 || title.Length > 300 || double.IsNaN(start) || double.IsInfinity(start) || start < 0)
            {
                continue;
            }
            entities.Add(new AiChapterEntity
            {
                MediaItemId = mediaItemId,
                Title = title,
                StartSeconds = start,
                CreatedAt = DateTime.UtcNow,
            });
        }
        entities.Sort((a, b) => a.StartSeconds.CompareTo(b.StartSeconds));
        return entities;
    }
}
