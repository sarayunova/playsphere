using PlaySphere.Application.Library;
using PlaySphere.Domain.Library;
using Xunit;

namespace PlaySphere.IntegrationTests;

/// <summary>
/// Integration tests for the AI persistence layer (PICKLE.md §42 tables <c>transcripts</c>,
/// <c>transcript_segments</c>, <c>chapters</c>, <c>ai_artifacts</c>; AI-03..08) against a real
/// migrated SQLite database, mirroring the bookmark/playlist repository tests.
/// </summary>
public sealed class AiPersistenceRepositoryTests : IDisposable
{
    private readonly LibraryDatabaseFixture _fixture = new();
    private IMediaLibraryRepository Repo => _fixture.Repository;

    private int _folderId;

    public void Dispose() => _fixture.Dispose();

    private async Task<int> NewMediaItemAsync(string filePath, string fingerprint)
    {
        _folderId = _folderId == 0 ? (await Repo.AddFolderAsync(Path.GetTempPath())).Id : _folderId;
        var item = await Repo.UpsertMediaItemAsync(new LibraryMediaItem
        {
            FilePath = filePath,
            Fingerprint = fingerprint,
            Title = filePath,
            LibraryFolderId = _folderId,
        });
        return item.Id;
    }

    [Fact]
    public async Task SaveTranscriptAsync_persists_segments_in_order()
    {
        var mediaId = await NewMediaItemAsync("t1.mp4", "fp-t-1");

        var transcript = await Repo.SaveTranscriptAsync(
            new AiTranscript { MediaItemId = mediaId, Language = "en", SourceModel = "whisper-1" },
            [
                new AiTranscriptSegment { StartSeconds = 0.0, EndSeconds = 1.5, Text = "Hello there." },
                new AiTranscriptSegment { StartSeconds = 1.5, EndSeconds = 3.0, Text = "Welcome to playback." },
            ]);

        Assert.True(transcript.Id > 0);
        Assert.NotEqual(default, transcript.CreatedAt);

        var loaded = await Repo.GetTranscriptAsync(mediaId);
        Assert.NotNull(loaded);
        Assert.Equal("en", loaded.Language);
        Assert.Equal("whisper-1", loaded.SourceModel);
        Assert.NotNull(loaded.Segments);
        Assert.Equal(2, loaded.Segments.Count);
        Assert.Equal(0, loaded.Segments[0].Ordinal);
        Assert.Equal("Hello there.", loaded.Segments[0].Text);
        Assert.Equal(1, loaded.Segments[1].Ordinal);
        Assert.Equal("Welcome to playback.", loaded.Segments[1].Text);
    }

    [Fact]
    public async Task SaveTranscriptAsync_replaces_the_previous_transcript_latest_wins()
    {
        var mediaId = await NewMediaItemAsync("t2.mp4", "fp-t-2");

        await Repo.SaveTranscriptAsync(
            new AiTranscript { MediaItemId = mediaId, Language = "en" },
            [new AiTranscriptSegment { StartSeconds = 0, EndSeconds = 1, Text = "Old" }]);

        await Repo.SaveTranscriptAsync(
            new AiTranscript { MediaItemId = mediaId, Language = "fr" },
            [new AiTranscriptSegment { StartSeconds = 0, EndSeconds = 1, Text = "Nouveau" }]);

        var loaded = await Repo.GetTranscriptAsync(mediaId);
        Assert.NotNull(loaded);
        Assert.Equal("fr", loaded.Language);
        Assert.NotNull(loaded.Segments);
        var segment = Assert.Single(loaded.Segments);
        Assert.Equal("Nouveau", segment.Text);
    }

    [Fact]
    public async Task GetTranscriptAsync_returns_null_when_none_saved()
    {
        var mediaId = await NewMediaItemAsync("t3.mp4", "fp-t-3");

        Assert.Null(await Repo.GetTranscriptAsync(mediaId));
    }

    [Fact]
    public async Task DeleteTranscriptAsync_removes_transcript_and_segments()
    {
        var mediaId = await NewMediaItemAsync("t4.mp4", "fp-t-4");

        await Repo.SaveTranscriptAsync(
            new AiTranscript { MediaItemId = mediaId, Language = "en" },
            [new AiTranscriptSegment { StartSeconds = 0, EndSeconds = 1, Text = "Bye" }]);

        await Repo.DeleteTranscriptAsync(mediaId);

        Assert.Null(await Repo.GetTranscriptAsync(mediaId));
    }

    [Fact]
    public async Task SaveChaptersAsync_persists_and_get_orders_by_start()
    {
        var mediaId = await NewMediaItemAsync("c1.mp4", "fp-c-1");

        await Repo.SaveChaptersAsync(mediaId,
        [
            new AiChapterEntity { Title = "Intro", StartSeconds = 0 },
            new AiChapterEntity { Title = "Middle", StartSeconds = 45.0 },
        ]);

        var chapters = await Repo.GetChaptersAsync(mediaId);
        Assert.Equal(2, chapters.Count);
        Assert.Equal("Intro", chapters[0].Title);
        Assert.Equal("Middle", chapters[1].Title);
    }

    [Fact]
    public async Task SaveChaptersAsync_replaces_the_previous_generation()
    {
        var mediaId = await NewMediaItemAsync("c2.mp4", "fp-c-2");

        await Repo.SaveChaptersAsync(mediaId, [new AiChapterEntity { Title = "Old", StartSeconds = 0 }]);
        await Repo.SaveChaptersAsync(mediaId, [new AiChapterEntity { Title = "New", StartSeconds = 5.0 }]);

        var chapters = await Repo.GetChaptersAsync(mediaId);
        var chapter = Assert.Single(chapters);
        Assert.Equal("New", chapter.Title);
    }

    [Fact]
    public async Task SaveAndGetArtifactsAsync_persists_and_orders_by_creation()
    {
        var mediaId = await NewMediaItemAsync("a1.mp4", "fp-a-1");

        var first = await Repo.SaveArtifactAsync(new AiArtifact { MediaItemId = mediaId, Key = "summary", PayloadJson = "{\"text\":\"one\"}" });
        var second = await Repo.SaveArtifactAsync(new AiArtifact { MediaItemId = mediaId, Key = "keypoint", PayloadJson = "{\"text\":\"two\"}", StartSeconds = 12.5 });

        var artifacts = await Repo.GetArtifactsAsync(mediaId);
        Assert.Equal(2, artifacts.Count);
        Assert.Equal(first.Id, artifacts[0].Id);
        Assert.Equal(second.Id, artifacts[1].Id);
        Assert.Equal(12.5, artifacts[1].StartSeconds);
    }

    [Fact]
    public async Task DeleteArtifactsAsync_removes_all_artifacts_for_an_item()
    {
        var mediaId = await NewMediaItemAsync("a2.mp4", "fp-a-2");

        await Repo.SaveArtifactAsync(new AiArtifact { MediaItemId = mediaId, Key = "summary", PayloadJson = "{}" });

        await Repo.DeleteArtifactsAsync(mediaId);

        Assert.Empty(await Repo.GetArtifactsAsync(mediaId));
    }
}
