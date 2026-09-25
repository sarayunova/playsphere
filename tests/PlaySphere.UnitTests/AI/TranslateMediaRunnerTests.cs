using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PlaySphere.AI;
using PlaySphere.Application.AI;
using PlaySphere.Application.Library;
using PlaySphere.Domain.AI;
using PlaySphere.Domain.Library;
using Xunit;

namespace PlaySphere.UnitTests.AI;

public class TranslateMediaRunnerTests : IDisposable
{
    private readonly string _mediaPath = Path.Combine(Path.GetTempPath(), "ps-ai-translations", "media.bin");

    public TranslateMediaRunnerTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_mediaPath)!);
        File.WriteAllBytes(_mediaPath, new byte[] { 1, 2, 3, 4 });
    }

    public void Dispose()
    {
        try { File.Delete(_mediaPath); } catch { }
        try { Directory.Delete(Path.GetDirectoryName(_mediaPath)!, recursive: true); } catch { }
    }

    private sealed class FakeAiService : IAiMediaService
    {
        public Func<Transcript, string, CancellationToken, IReadOnlyList<TranscriptSegment>> Handler { get; set; } =
            (t, _, _) => t.Segments;

        public Task<IReadOnlyList<TranscriptSegment>> TranslateAsync(Transcript transcript, string targetLanguage, CancellationToken cancellationToken = default)
            => Task.FromResult(Handler(transcript, targetLanguage, cancellationToken));

        public Task<Transcript> TranscribeAudioAsync(string audioFilePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AiChapter>> GenerateChaptersAsync(Transcript transcript, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiSummary> SummarizeAsync(Transcript transcript, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AiKeyPoint>> AskAsync(Transcript transcript, string question, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeRepo : IMediaLibraryRepository
    {
        public LibraryMediaItem? Item { get; set; }
        public AiTranscript? Transcript { get; set; }
        public AiArtifact? SavedArtifact { get; private set; }
        public bool ThrowOnFind { get; set; }
        public bool ThrowOnTranscript { get; set; }
        public bool ThrowOnSave { get; set; }

        public Task<LibraryMediaItem?> FindByPathAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (ThrowOnFind) throw new InvalidOperationException("boom-find");
            return Task.FromResult(Item);
        }

        public Task<AiTranscript?> GetTranscriptAsync(int mediaItemId, CancellationToken cancellationToken = default)
        {
            if (ThrowOnTranscript) throw new InvalidOperationException("boom-transcript");
            return Task.FromResult(Transcript);
        }

        public Task<AiArtifact> SaveArtifactAsync(AiArtifact artifact, CancellationToken cancellationToken = default)
        {
            if (ThrowOnSave) throw new InvalidOperationException("boom-save");
            SavedArtifact = artifact;
            return Task.FromResult(artifact);
        }

        public Task<LibraryFolder> AddFolderAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LibraryFolder> EnsureFolderAsync(string path, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveFolderAsync(int folderId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LibraryFolder>> GetFoldersAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LibraryMediaItem> UpsertMediaItemAsync(LibraryMediaItem item, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<LibraryMediaItem?> FindByFingerprintAsync(string fingerprint, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveMediaItemAsync(int mediaItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LibraryMediaItem>> GetAllMediaItemsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LibraryMediaItem>> GetFavoritesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LibraryMediaItem>> GetContinueWatchingAsync(int maxCount = 20, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LibraryMediaItem>> GetRecentlyPlayedAsync(int maxCount = 20, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<LibraryMediaItem>> SearchAsync(string query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetFavoriteAsync(int mediaItemId, bool isFavorite, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RecordPlaybackAsync(int mediaItemId, TimeSpan position, TimeSpan duration, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetCoverArtPathAsync(int mediaItemId, string? coverArtPath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Bookmark> AddBookmarkAsync(int mediaItemId, long positionMs, string? label = null, string? note = null, string? category = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Bookmark>> GetBookmarksAsync(int mediaItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveBookmarkAsync(int bookmarkId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Playlist> CreatePlaylistAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RenamePlaylistAsync(int playlistId, string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeletePlaylistAsync(int playlistId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<Playlist>> GetPlaylistsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PlaylistItem> AddToPlaylistAsync(int playlistId, int mediaItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddRangeToPlaylistAsync(int playlistId, IReadOnlyList<int> mediaItemIds, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task RemoveFromPlaylistAsync(int playlistItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ReorderPlaylistAsync(int playlistId, IReadOnlyList<int> mediaItemIdsInOrder, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PlaylistItem>> GetPlaylistItemsAsync(int playlistId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiTranscript> SaveTranscriptAsync(AiTranscript transcript, IReadOnlyList<AiTranscriptSegment> segments, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteTranscriptAsync(int mediaItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveChaptersAsync(int mediaItemId, IReadOnlyList<AiChapterEntity> chapters, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AiChapterEntity>> GetChaptersAsync(int mediaItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AiArtifact>> GetArtifactsAsync(int mediaItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteArtifactsAsync(int mediaItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private static AiTranscript TranscriptWith(params (double start, double end, string text)[] segs)
    {
        var transcript = new AiTranscript { Id = 7, MediaItemId = 3, Language = "en" };
        var list = new List<AiTranscriptSegment>();
        for (var i = 0; i < segs.Length; i++)
        {
            list.Add(new AiTranscriptSegment { Ordinal = i, StartSeconds = segs[i].start, EndSeconds = segs[i].end, Text = segs[i].text });
        }
        transcript.Segments = list;
        return transcript;
    }

    private (TranslateMediaRunner runner, FakeAiService ai, FakeRepo repo) Build(
        LibraryMediaItem? item,
        AiTranscript? transcript,
        Func<Transcript, string, CancellationToken, IReadOnlyList<TranscriptSegment>>? translate = null)
    {
        var ai = new FakeAiService();
        if (translate != null) ai.Handler = translate;
        var repo = new FakeRepo { Item = item, Transcript = transcript };
        return (new TranslateMediaRunner(ai, repo), ai, repo);
    }

    [Fact]
    public async Task TranslateAsync_missing_file_fails_softly()
    {
        var (runner, _, repo) = Build(new LibraryMediaItem { Id = 1, FilePath = _mediaPath, Fingerprint = "fp-1" }, TranscriptWith((0, 5, "Hi.")));
        var result = await runner.TranslateAsync(Path.Combine(Path.GetTempPath(), "ps-ai-translations", "nope.bin"), "French");
        Assert.False(result.Success);
        Assert.Null(repo.SavedArtifact);
    }

    [Fact]
    public async Task TranslateAsync_blank_language_fails_softly()
    {
        var (runner, _, repo) = Build(new LibraryMediaItem { Id = 1, FilePath = _mediaPath, Fingerprint = "fp-1" }, TranscriptWith((0, 5, "Hi.")));
        var result = await runner.TranslateAsync(_mediaPath, "   ");
        Assert.False(result.Success);
        Assert.Contains("language", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(repo.SavedArtifact);
    }

    [Fact]
    public async Task TranslateAsync_non_cataloged_file_fails_softly()
    {
        var (runner, _, repo) = Build(null, TranscriptWith((0, 5, "Hi.")));
        var result = await runner.TranslateAsync(_mediaPath, "French");
        Assert.False(result.Success);
        Assert.Contains("library", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(repo.SavedArtifact);
    }

    [Fact]
    public async Task TranslateAsync_no_transcript_yet_fails_with_guidance()
    {
        var (runner, _, repo) = Build(new LibraryMediaItem { Id = 1, FilePath = _mediaPath, Fingerprint = "fp-1" }, null);
        var result = await runner.TranslateAsync(_mediaPath, "French");
        Assert.False(result.Success);
        Assert.Contains("transcript", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(repo.SavedArtifact);
    }

    [Fact]
    public async Task TranslateAsync_provider_failure_fails_softly()
    {
        var (runner, _, repo) = Build(
            new LibraryMediaItem { Id = 1, FilePath = _mediaPath, Fingerprint = "fp-1" },
            TranscriptWith((0, 5, "Hi.")),
            translate: (_, _, _) => throw new AiException("No OpenAI API key configured."));
        var result = await runner.TranslateAsync(_mediaPath, "French");
        Assert.False(result.Success);
        Assert.Contains("No OpenAI API key", result.ErrorMessage);
        Assert.Null(repo.SavedArtifact);
    }

    [Fact]
    public async Task TranslateAsync_empty_result_fails()
    {
        var (runner, _, repo) = Build(
            new LibraryMediaItem { Id = 1, FilePath = _mediaPath, Fingerprint = "fp-1" },
            TranscriptWith((0, 5, "Hi.")),
            translate: (_, _, _) => Array.Empty<TranscriptSegment>());
        var result = await runner.TranslateAsync(_mediaPath, "French");
        Assert.False(result.Success);
        Assert.Null(repo.SavedArtifact);
    }

    [Fact]
    public async Task TranslateAsync_success_passes_the_exact_transcript_and_language_through()
    {
        Transcript? seenTranscript = null;
        string? seenLanguage = null;
        var (runner, _, repo) = Build(
            new LibraryMediaItem { Id = 3, FilePath = _mediaPath, Fingerprint = "fp-3" },
            TranscriptWith((0, 5, "Bonjour."), (5, 15, "Au revoir.")),
            translate: (t, lang, _) =>
            {
                seenTranscript = t;
                seenLanguage = lang;
                return new[] { new TranscriptSegment(0, 5, "Hello."), new TranscriptSegment(5, 15, "Goodbye.") };
            });

        var result = await runner.TranslateAsync(_mediaPath, "  English  ");

        Assert.True(result.Success);
        Assert.Equal("English", result.TargetLanguage);
        Assert.NotNull(result.Segments);
        Assert.Equal(2, result.Segments!.Count);
        Assert.Equal("Hello.", result.Segments[0].Text);
        Assert.Equal(0, result.Segments[0].StartSeconds);
        Assert.Equal(5, result.Segments[0].EndSeconds);

        Assert.Equal("English", seenLanguage);
        Assert.Equal(2, seenTranscript!.Segments.Count);
        Assert.Equal("Bonjour.", seenTranscript.Segments[0].Text);

        Assert.NotNull(repo.SavedArtifact);
        Assert.Equal(3, repo.SavedArtifact!.MediaItemId);
        Assert.Equal("translation:english", repo.SavedArtifact.Key);
        Assert.Contains("Hello", repo.SavedArtifact.PayloadJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TranslateAsync_persistence_failure_is_best_effort_but_still_succeeds()
    {
        var (runner, _, repo) = Build(
            new LibraryMediaItem { Id = 3, FilePath = _mediaPath, Fingerprint = "fp-3" },
            TranscriptWith((0, 5, "Hi.")),
            translate: (_, _, _) => new[] { new TranscriptSegment(0, 5, "Still returned.") });
        repo.ThrowOnSave = true;

        var result = await runner.TranslateAsync(_mediaPath, "French");

        Assert.True(result.Success);
        Assert.NotNull(result.Segments);
        Assert.Equal("Still returned.", result.Segments![0].Text);
    }

    [Fact]
    public async Task TranslateAsync_library_lookup_failure_fails_softly()
    {
        var (runner, _, repo) = Build(
            new LibraryMediaItem { Id = 1, FilePath = _mediaPath, Fingerprint = "fp-1" },
            TranscriptWith((0, 5, "Hi.")));
        repo.ThrowOnFind = true;
        var result = await runner.TranslateAsync(_mediaPath, "French");
        Assert.False(result.Success);
        Assert.Null(repo.SavedArtifact);
    }
}
