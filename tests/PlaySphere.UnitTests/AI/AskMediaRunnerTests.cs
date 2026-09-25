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

public class AskMediaRunnerTests : IDisposable
{
    private readonly string _mediaPath = Path.Combine(Path.GetTempPath(), "ps-ai-ask", "media.bin");

    public AskMediaRunnerTests()
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
        public Func<Transcript, string, CancellationToken, IReadOnlyList<AiKeyPoint>> Handler { get; set; } =
            (_, _, _) => new[] { new AiKeyPoint("The feature ships next month.", 42.5) };

        public Task<IReadOnlyList<AiKeyPoint>> AskAsync(Transcript transcript, string question, CancellationToken cancellationToken = default)
            => Task.FromResult(Handler(transcript, question, cancellationToken));

        public Task<Transcript> TranscribeAudioAsync(string audioFilePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AiChapter>> GenerateChaptersAsync(Transcript transcript, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiSummary> SummarizeAsync(Transcript transcript, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TranscriptSegment>> TranslateAsync(Transcript transcript, string targetLanguage, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeRepo : IMediaLibraryRepository
    {
        public LibraryMediaItem? Item { get; set; }
        public AiTranscript? Transcript { get; set; }
        public bool ThrowOnFind { get; set; }
        public bool ThrowOnTranscript { get; set; }

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
        public Task<AiArtifact> SaveArtifactAsync(AiArtifact artifact, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

    private (AskMediaRunner runner, FakeAiService ai, FakeRepo repo) Build(
        LibraryMediaItem? item,
        AiTranscript? transcript,
        Func<Transcript, string, CancellationToken, IReadOnlyList<AiKeyPoint>>? ask = null)
    {
        var ai = new FakeAiService();
        if (ask != null) ai.Handler = ask;
        var repo = new FakeRepo { Item = item, Transcript = transcript };
        return (new AskMediaRunner(ai, repo), ai, repo);
    }

    [Fact]
    public async Task AskAsync_blank_question_fails_softly()
    {
        var (runner, _, repo) = Build(new LibraryMediaItem { Id = 1, FilePath = _mediaPath, Fingerprint = "fp-1" }, TranscriptWith((0, 5, "Hi.")));
        var result = await runner.AskAsync(_mediaPath, "   ");
        Assert.False(result.Success);
        Assert.Contains("question", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AskAsync_missing_file_fails_softly()
    {
        var (runner, _, repo) = Build(new LibraryMediaItem { Id = 1, FilePath = _mediaPath, Fingerprint = "fp-1" }, TranscriptWith((0, 5, "Hi.")));
        var result = await runner.AskAsync(Path.Combine(Path.GetTempPath(), "ps-ai-ask", "nope.bin"), "What about it?");
        Assert.False(result.Success);
    }

    [Fact]
    public async Task AskAsync_non_cataloged_file_fails_softly()
    {
        var (runner, _, repo) = Build(null, TranscriptWith((0, 5, "Hi.")));
        var result = await runner.AskAsync(_mediaPath, "What about it?");
        Assert.False(result.Success);
        Assert.Contains("library", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AskAsync_no_transcript_yet_fails_with_guidance()
    {
        var (runner, _, repo) = Build(new LibraryMediaItem { Id = 1, FilePath = _mediaPath, Fingerprint = "fp-1" }, null);
        var result = await runner.AskAsync(_mediaPath, "What about it?");
        Assert.False(result.Success);
        Assert.Contains("transcript", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AskAsync_provider_failure_fails_softly()
    {
        var (runner, _, repo) = Build(
            new LibraryMediaItem { Id = 1, FilePath = _mediaPath, Fingerprint = "fp-1" },
            TranscriptWith((0, 5, "Hi.")),
            ask: (_, _, _) => throw new AiException("No OpenAI API key configured."));
        var result = await runner.AskAsync(_mediaPath, "What about it?");
        Assert.False(result.Success);
        Assert.Contains("No OpenAI API key", result.ErrorMessage);
    }

    [Fact]
    public async Task AskAsync_empty_answers_fails()
    {
        var (runner, _, repo) = Build(
            new LibraryMediaItem { Id = 1, FilePath = _mediaPath, Fingerprint = "fp-1" },
            TranscriptWith((0, 5, "Hi.")),
            ask: (_, _, _) => Array.Empty<AiKeyPoint>());
        var result = await runner.AskAsync(_mediaPath, "What about it?");
        Assert.False(result.Success);
    }

    [Fact]
    public async Task AskAsync_success_passes_question_and_returns_timestamped_answers()
    {
        string? asked = null;
        var (runner, _, repo) = Build(
            new LibraryMediaItem { Id = 3, FilePath = _mediaPath, Fingerprint = "fp-3" },
            TranscriptWith((0, 5, "Intro."), (5, 15, "Main.")),
            ask: (_, q, _) =>
            {
                asked = q;
                return new[] { new AiKeyPoint("The feature ships next month.", 42.5), new AiKeyPoint("No timestamp.", null) };
            });

        var result = await runner.AskAsync(_mediaPath, "When does it ship?");

        Assert.True(result.Success);
        Assert.Equal("When does it ship?", asked);
        Assert.NotNull(result.Answers);
        Assert.Collection(result.Answers!,
            a => { Assert.Equal("The feature ships next month.", a.Text); Assert.Equal(42.5, a.StartSeconds); },
            a => { Assert.Equal("No timestamp.", a.Text); Assert.Null(a.StartSeconds); });
    }

    [Fact]
    public async Task AskAsync_library_lookup_failure_fails_softly()
    {
        var (runner, _, repo) = Build(
            new LibraryMediaItem { Id = 1, FilePath = _mediaPath, Fingerprint = "fp-1" },
            TranscriptWith((0, 5, "Hi.")));
        repo.ThrowOnFind = true;
        var result = await runner.AskAsync(_mediaPath, "What about it?");
        Assert.False(result.Success);
    }
}
