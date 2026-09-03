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

public class GenerateChaptersRunnerTests : IDisposable
{
    private readonly string _mediaPath = Path.Combine(Path.GetTempPath(), "ps-ai-chapters", "media.bin");

    public GenerateChaptersRunnerTests()
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
        public Func<Transcript, CancellationToken, IReadOnlyList<AiChapter>> Handler { get; set; } =
            (_, _) => new[] { new AiChapter("Intro", 0), new AiChapter("Main", 12.5) };

        public Task<IReadOnlyList<AiChapter>> GenerateChaptersAsync(Transcript transcript, CancellationToken cancellationToken = default)
            => Task.FromResult(Handler(transcript, cancellationToken));

        public Task<Transcript> TranscribeAudioAsync(string audioFilePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiSummary> SummarizeAsync(Transcript transcript, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AiKeyPoint>> AskAsync(Transcript transcript, string question, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeRepo : IMediaLibraryRepository
    {
        public LibraryMediaItem? Item { get; set; }
        public AiTranscript? Transcript { get; set; }
        public IReadOnlyList<AiChapterEntity>? SavedChapters { get; private set; }
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

        public Task SaveChaptersAsync(int mediaItemId, IReadOnlyList<AiChapterEntity> chapters, CancellationToken cancellationToken = default)
        {
            SavedChapters = chapters;
            return Task.CompletedTask;
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

    private (GenerateChaptersRunner runner, FakeAiService ai, FakeRepo repo) Build(
        LibraryMediaItem? item,
        AiTranscript? transcript,
        Func<Transcript, CancellationToken, IReadOnlyList<AiChapter>>? generate = null)
    {
        var ai = new FakeAiService();
        if (generate != null) ai.Handler = generate;
        var repo = new FakeRepo { Item = item, Transcript = transcript };
        return (new GenerateChaptersRunner(ai, repo), ai, repo);
    }

    [Fact]
    public async Task GenerateChaptersAsync_missing_file_fails_softly()
    {
        var (runner, _, repo) = Build(new LibraryMediaItem { Id = 1, FilePath = _mediaPath, Fingerprint = "fp-1" }, TranscriptWith((0, 5, "Hi.")));
        var result = await runner.GenerateChaptersAsync(Path.Combine(Path.GetTempPath(), "ps-ai-chapters", "nope.bin"));
        Assert.False(result.Success);
        Assert.Null(repo.SavedChapters);
    }

    [Fact]
    public async Task GenerateChaptersAsync_non_cataloged_file_fails_softly()
    {
        var (runner, _, repo) = Build(null, TranscriptWith((0, 5, "Hi.")));
        var result = await runner.GenerateChaptersAsync(_mediaPath);
        Assert.False(result.Success);
        Assert.Contains("library", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(repo.SavedChapters);
    }

    [Fact]
    public async Task GenerateChaptersAsync_no_transcript_yet_fails_with_guidance()
    {
        var (runner, _, repo) = Build(new LibraryMediaItem { Id = 1, FilePath = _mediaPath, Fingerprint = "fp-1" }, null);
        var result = await runner.GenerateChaptersAsync(_mediaPath);
        Assert.False(result.Success);
        Assert.Contains("transcript", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(repo.SavedChapters);
    }

    [Fact]
    public async Task GenerateChaptersAsync_provider_failure_fails_softly()
    {
        var (runner, _, repo) = Build(
            new LibraryMediaItem { Id = 1, FilePath = _mediaPath, Fingerprint = "fp-1" },
            TranscriptWith((0, 5, "Hi.")),
            generate: (_, _) => throw new AiException("No OpenAI API key configured."));
        var result = await runner.GenerateChaptersAsync(_mediaPath);
        Assert.False(result.Success);
        Assert.Contains("No OpenAI API key", result.ErrorMessage);
        Assert.Null(repo.SavedChapters);
    }

    [Fact]
    public async Task GenerateChaptersAsync_empty_result_fails()
    {
        var (runner, _, repo) = Build(
            new LibraryMediaItem { Id = 1, FilePath = _mediaPath, Fingerprint = "fp-1" },
            TranscriptWith((0, 5, "Hi.")),
            generate: (_, _) => Array.Empty<AiChapter>());
        var result = await runner.GenerateChaptersAsync(_mediaPath);
        Assert.False(result.Success);
        Assert.Null(repo.SavedChapters);
    }

    [Fact]
    public async Task GenerateChaptersAsync_success_persists_ordered_chapters()
    {
        var (runner, _, repo) = Build(
            new LibraryMediaItem { Id = 3, FilePath = _mediaPath, Fingerprint = "fp-3" },
            TranscriptWith((0, 5, "Intro."), (5, 15, "Main.")),
            generate: (_, _) => new[] { new AiChapter("Main", 5), new AiChapter("Intro", 0) });

        var result = await runner.GenerateChaptersAsync(_mediaPath);

        Assert.True(result.Success);
        Assert.NotNull(result.Chapters);
        Assert.Collection(result.Chapters!,
            c => { Assert.Equal("Intro", c.Title); Assert.Equal(0, c.StartSeconds); },
            c => { Assert.Equal("Main", c.Title); Assert.Equal(5, c.StartSeconds); });

        Assert.NotNull(repo.SavedChapters);
        Assert.Collection(repo.SavedChapters!,
            c => Assert.Equal(0, c.StartSeconds),
            c => Assert.Equal(5, c.StartSeconds));
        Assert.All(repo.SavedChapters!, c => Assert.Equal(3, c.MediaItemId));
    }

    [Fact]
    public async Task GenerateChaptersAsync_drops_invalid_generated_rows()
    {
        var (runner, _, repo) = Build(
            new LibraryMediaItem { Id = 3, FilePath = _mediaPath, Fingerprint = "fp-3" },
            TranscriptWith((0, 5, "Intro.")),
            generate: (_, _) => new[]
            {
                new AiChapter("Good", 3),
                new AiChapter("   ", -2),
                new AiChapter(new string('x', 400), 1),
                new AiChapter("NaN", double.NaN),
            });

        var result = await runner.GenerateChaptersAsync(_mediaPath);

        Assert.True(result.Success);
        Assert.NotNull(result.Chapters);
        Assert.Collection(result.Chapters!, c => Assert.Equal("Good", c.Title));
    }

    [Fact]
    public async Task GenerateChaptersAsync_library_lookup_failure_fails_softly()
    {
        var (runner, _, repo) = Build(
            new LibraryMediaItem { Id = 1, FilePath = _mediaPath, Fingerprint = "fp-1" },
            TranscriptWith((0, 5, "Hi.")));
        repo.ThrowOnFind = true;
        var result = await runner.GenerateChaptersAsync(_mediaPath);
        Assert.False(result.Success);
        Assert.Null(repo.SavedChapters);
    }
}
