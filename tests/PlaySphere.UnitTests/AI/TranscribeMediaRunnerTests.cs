using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PlaySphere.AI;
using PlaySphere.Application.AI;
using PlaySphere.Application.Library;
using PlaySphere.Application.Media;
using PlaySphere.Domain.AI;
using PlaySphere.Domain.Library;
using PlaySphere.Domain.Media;
using Xunit;

namespace PlaySphere.UnitTests.AI;

public class TranscribeMediaRunnerTests : IDisposable
{
    private readonly string _mediaPath = Path.Combine(Path.GetTempPath(), "ps-ai-runner", "media.bin");

    public TranscribeMediaRunnerTests()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_mediaPath)!);
        File.WriteAllBytes(_mediaPath, new byte[] { 1, 2, 3, 4 });
    }

    public void Dispose()
    {
        try { File.Delete(_mediaPath); } catch { }
        try { Directory.Delete(Path.GetDirectoryName(_mediaPath)!, recursive: true); } catch { }
    }

    private sealed class FakeAudioExtractor : IAudioExtractor
    {
        public Func<string, string, CancellationToken, AudioExtractionResult> Handler { get; set; } =
            (_, outPath, _) => { File.WriteAllBytes(outPath, new byte[] { 0x52, 0x49 }); return AudioExtractionResult.Ok(outPath); };
        public string? LastOutputPath { get; private set; }
        public Task<AudioExtractionResult> ExtractAudioAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
        {
            LastOutputPath = outputPath;
            return Task.FromResult(Handler(inputPath, outputPath, cancellationToken));
        }
    }

    private sealed class FakeAiService : IAiMediaService
    {
        public Func<string, CancellationToken, Transcript> Handler { get; set; } =
            (_, _) => new Transcript(new[] { new TranscriptSegment(0, 5, "Hello world.") }, "en");

        public Task<Transcript> TranscribeAudioAsync(string audioFilePath, CancellationToken cancellationToken = default)
            => Task.FromResult(Handler(audioFilePath, cancellationToken));

        public Task<IReadOnlyList<AiChapter>> GenerateChaptersAsync(Transcript transcript, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<AiSummary> SummarizeAsync(Transcript transcript, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<IReadOnlyList<AiKeyPoint>> AskAsync(Transcript transcript, string question, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class FakeRepo : IMediaLibraryRepository
    {
        public LibraryMediaItem? Item { get; set; }
        public AiTranscript? Persisted { get; private set; }
        public IReadOnlyList<AiTranscriptSegment>? PersistedSegments { get; private set; }
        public bool ThrowOnFind { get; set; }

        public Task<LibraryMediaItem?> FindByPathAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (ThrowOnFind) throw new InvalidOperationException("boom");
            return Task.FromResult(Item);
        }

        public Task<AiTranscript> SaveTranscriptAsync(AiTranscript transcript, IReadOnlyList<AiTranscriptSegment> segments, CancellationToken cancellationToken = default)
        {
            transcript.Id = 42;
            Persisted = transcript;
            PersistedSegments = segments;
            return Task.FromResult(transcript);
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
        public Task<AiTranscript?> GetTranscriptAsync(int mediaItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteTranscriptAsync(int mediaItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveChaptersAsync(int mediaItemId, IReadOnlyList<AiChapterEntity> chapters, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AiChapterEntity>> GetChaptersAsync(int mediaItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<AiArtifact> SaveArtifactAsync(AiArtifact artifact, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<AiArtifact>> GetArtifactsAsync(int mediaItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteArtifactsAsync(int mediaItemId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private (TranscribeMediaRunner runner, FakeAudioExtractor extractor, FakeAiService ai, FakeRepo repo) Build(
        LibraryMediaItem? item,
        Func<string, CancellationToken, Transcript>? transcribe = null,
        Func<string, string, CancellationToken, AudioExtractionResult>? extract = null)
    {
        var extractor = new FakeAudioExtractor();
        if (extract != null) extractor.Handler = extract;
        var ai = new FakeAiService();
        if (transcribe != null) ai.Handler = transcribe;
        var repo = new FakeRepo { Item = item };
        var runner = new TranscribeMediaRunner(extractor, ai, repo);
        return (runner, extractor, ai, repo);
    }

    [Fact]
    public async Task RunAsync_missing_file_fails_softly()
    {
        var (runner, _, _, repo) = Build(new LibraryMediaItem { Id = 1, FilePath = _mediaPath, Fingerprint = "fp-1" });
        var result = await runner.RunAsync(Path.Combine(Path.GetTempPath(), "ps-ai-runner", "nope.bin"));
        Assert.False(result.Success);
        Assert.NotNull(result.ErrorMessage);
        Assert.Null(repo.Persisted);
    }

    [Fact]
    public async Task RunAsync_non_cataloged_file_fails_softly()
    {
        var (runner, _, _, repo) = Build(null);
        var result = await runner.RunAsync(_mediaPath);
        Assert.False(result.Success);
        Assert.Contains("library", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(repo.Persisted);
    }

    [Fact]
    public async Task RunAsync_extraction_failure_fails_softly()
    {
        var (runner, _, _, repo) = Build(
            new LibraryMediaItem { Id = 1, FilePath = _mediaPath, Fingerprint = "fp-1" },
            extract: (_, _, _) => AudioExtractionResult.Fail("ffmpeg missing"));
        var result = await runner.RunAsync(_mediaPath);
        Assert.False(result.Success);
        Assert.Contains("ffmpeg missing", result.ErrorMessage);
        Assert.Null(repo.Persisted);
    }

    [Fact]
    public async Task RunAsync_provider_failure_fails_softly()
    {
        var (runner, _, _, repo) = Build(
            new LibraryMediaItem { Id = 1, FilePath = _mediaPath, Fingerprint = "fp-1" },
            transcribe: (_, _) => throw new AiException("No OpenAI API key configured."));
        var result = await runner.RunAsync(_mediaPath);
        Assert.False(result.Success);
        Assert.Contains("No OpenAI API key", result.ErrorMessage);
        Assert.Null(repo.Persisted);
    }

    [Fact]
    public async Task RunAsync_empty_transcript_fails()
    {
        var (runner, _, _, repo) = Build(
            new LibraryMediaItem { Id = 1, FilePath = _mediaPath, Fingerprint = "fp-1" },
            transcribe: (_, _) => new Transcript([], "en"));
        var result = await runner.RunAsync(_mediaPath);
        Assert.False(result.Success);
        Assert.Null(repo.Persisted);
    }

    [Fact]
    public async Task RunAsync_success_persists_and_returns_transcript()
    {
        var transcript = new Transcript(
            new[]
            {
                new TranscriptSegment(0, 4, "First line."),
                new TranscriptSegment(4, 9, "Second line."),
            },
            "en-EN");
        var (runner, _, _, repo) = Build(
            new LibraryMediaItem { Id = 7, FilePath = _mediaPath, Fingerprint = "fp-7" },
            transcribe: (_, _) => transcript);

        var result = await runner.RunAsync(_mediaPath);

        Assert.True(result.Success);
        Assert.Equal(2, result.SegmentCount);
        Assert.NotNull(result.Transcript);
        Assert.Equal("en-EN", result.Transcript.Language);

        Assert.NotNull(repo.Persisted);
        Assert.Equal(7, repo.Persisted.MediaItemId);
        Assert.Equal("en-EN", repo.Persisted.Language);
        Assert.NotNull(repo.PersistedSegments);
        Assert.Equal(2, repo.PersistedSegments.Count);
        Assert.Equal(0, repo.PersistedSegments[0].Ordinal);
        Assert.Equal("Second line.", repo.PersistedSegments[1].Text);
    }

    [Fact]
    public async Task RunAsync_cleans_up_temp_wav_on_success()
    {
        string? capturedOutput = null;
        var (runner, extractor, _, repo) = Build(
            new LibraryMediaItem { Id = 1, FilePath = _mediaPath, Fingerprint = "fp-1" },
            extract: (_, outPath, _) =>
            {
                capturedOutput = outPath;
                File.WriteAllBytes(outPath, new byte[] { 1 });
                return AudioExtractionResult.Ok(outPath);
            });

        await runner.RunAsync(_mediaPath);

        Assert.NotNull(capturedOutput);
        Assert.False(File.Exists(capturedOutput), "Temp WAV should be deleted after the run.");
        Assert.False(Directory.Exists(Path.GetDirectoryName(capturedOutput)!), "Temp dir should be removed.");
    }
}
