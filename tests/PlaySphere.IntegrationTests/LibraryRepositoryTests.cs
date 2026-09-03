using PlaySphere.Application.Library;
using PlaySphere.Domain.Library;
using Xunit;

namespace PlaySphere.IntegrationTests;

/// <summary>
/// Tests for <see cref="IMediaLibraryRepository"/> against a real migrated SQLite database
/// (PICKLE.md §22/§23/§42). A fresh temp-file database per test (xunit creates a new test class
/// instance per [Fact]) — no shared state between tests.
/// </summary>
public sealed class LibraryRepositoryTests : IDisposable
{
    private readonly LibraryDatabaseFixture _fixture = new();
    private IMediaLibraryRepository Repo => _fixture.Repository;

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task AddFolderAsync_persists_and_canonicalizes_path()
    {
        var folder = await Repo.AddFolderAsync(Path.GetTempPath());

        Assert.True(folder.Id > 0);
        Assert.Equal(Path.GetFullPath(Path.GetTempPath()), folder.Path);
    }

    [Fact]
    public async Task AddFolderAsync_rejects_duplicate_path()
    {
        var path = Path.GetTempPath();
        await Repo.AddFolderAsync(path);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Repo.AddFolderAsync(path));
    }

    [Fact]
    public async Task EnsureFolderAsync_creates_or_returns_existing()
    {
        var path = Path.GetTempPath();

        var first = await Repo.EnsureFolderAsync(path);
        var second = await Repo.EnsureFolderAsync(path);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(await Repo.GetFoldersAsync());
    }

    [Fact]
    public async Task GetFoldersAsync_returns_added_folders()
    {
        await Repo.AddFolderAsync(Path.GetTempPath());

        var folders = await Repo.GetFoldersAsync();

        Assert.Single(folders);
    }

    [Fact]
    public async Task RemoveFolderAsync_cascades_media_items_and_history()
    {
        var folder = await Repo.AddFolderAsync(Path.GetTempPath());
        var item = await Repo.UpsertMediaItemAsync(NewItem("a.mp4", "fp-1", folder.Id));
        await Repo.RecordPlaybackAsync(item.Id, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10));

        await Repo.RemoveFolderAsync(folder.Id);

        Assert.Empty(await Repo.GetFoldersAsync());
        Assert.Null(await Repo.FindByFingerprintAsync("fp-1"));
    }

    [Fact]
    public async Task UpsertMediaItemAsync_inserts_new_item()
    {
        var folder = await Repo.AddFolderAsync(Path.GetTempPath());

        var saved = await Repo.UpsertMediaItemAsync(NewItem("movie.mp4", "fp-insert", folder.Id));

        Assert.True(saved.Id > 0);
        Assert.Equal("movie.mp4", saved.FilePath);
    }

    [Fact]
    public async Task UpsertMediaItemAsync_updates_existing_item_matched_by_fingerprint()
    {
        var folder = await Repo.AddFolderAsync(Path.GetTempPath());
        var first = await Repo.UpsertMediaItemAsync(NewItem("movie.mp4", "fp-update", folder.Id, title: "Old Title"));

        var updated = await Repo.UpsertMediaItemAsync(NewItem("movie.mp4", "fp-update", folder.Id, title: "New Title"));

        Assert.Equal(first.Id, updated.Id);
        Assert.Equal("New Title", updated.Title);
        Assert.Single(await Repo.GetAllMediaItemsAsync());
    }

    [Fact]
    public async Task FindByPathAsync_finds_item_by_file_path()
    {
        var folder = await Repo.AddFolderAsync(Path.GetTempPath());
        await Repo.UpsertMediaItemAsync(NewItem("unique-path.mp4", "fp-path", folder.Id));

        var found = await Repo.FindByPathAsync("unique-path.mp4");

        Assert.NotNull(found);
        Assert.Equal("fp-path", found!.Fingerprint);
    }

    [Fact]
    public async Task RemoveMediaItemAsync_removes_item_and_its_history()
    {
        var folder = await Repo.AddFolderAsync(Path.GetTempPath());
        var item = await Repo.UpsertMediaItemAsync(NewItem("a.mp4", "fp-remove", folder.Id));
        await Repo.RecordPlaybackAsync(item.Id, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10));

        await Repo.RemoveMediaItemAsync(item.Id);

        Assert.Null(await Repo.FindByFingerprintAsync("fp-remove"));
    }

    [Fact]
    public async Task SetFavoriteAsync_and_GetFavoritesAsync_round_trip()
    {
        var folder = await Repo.AddFolderAsync(Path.GetTempPath());
        var item = await Repo.UpsertMediaItemAsync(NewItem("a.mp4", "fp-fav", folder.Id));

        await Repo.SetFavoriteAsync(item.Id, true);

        var favorites = await Repo.GetFavoritesAsync();
        Assert.Single(favorites);
        Assert.Equal(item.Id, favorites[0].Id);
    }

    [Fact]
    public async Task RecordPlaybackAsync_updates_last_played_position_and_watched_percentage()
    {
        var folder = await Repo.AddFolderAsync(Path.GetTempPath());
        var item = await Repo.UpsertMediaItemAsync(NewItem("a.mp4", "fp-progress", folder.Id));

        await Repo.RecordPlaybackAsync(item.Id, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10));

        var updated = await Repo.FindByFingerprintAsync("fp-progress");
        Assert.NotNull(updated);
        Assert.Equal(TimeSpan.FromMinutes(5), updated!.LastPosition);
        Assert.Equal(50.0, updated.WatchedPercentage, precision: 3);
        Assert.NotNull(updated.LastPlayedAt);
    }

    [Fact]
    public async Task GetContinueWatchingAsync_excludes_unstarted_and_finished_items()
    {
        var folder = await Repo.AddFolderAsync(Path.GetTempPath());
        var unstarted = await Repo.UpsertMediaItemAsync(NewItem("unstarted.mp4", "fp-unstarted", folder.Id));
        var inProgress = await Repo.UpsertMediaItemAsync(NewItem("in-progress.mp4", "fp-in-progress", folder.Id));
        var finished = await Repo.UpsertMediaItemAsync(NewItem("finished.mp4", "fp-finished", folder.Id));

        // unstarted: never played, so no history recorded at all — WatchedPercentage stays 0.
        await Repo.RecordPlaybackAsync(inProgress.Id, TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(10));
        await Repo.RecordPlaybackAsync(finished.Id, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));

        var continueWatching = await Repo.GetContinueWatchingAsync();

        var ids = continueWatching.Select(m => m.Id).ToList();
        Assert.Contains(inProgress.Id, ids);
        Assert.DoesNotContain(unstarted.Id, ids);
        Assert.DoesNotContain(finished.Id, ids);
    }

    [Fact]
    public async Task GetRecentlyPlayedAsync_orders_most_recent_first()
    {
        var folder = await Repo.AddFolderAsync(Path.GetTempPath());
        var first = await Repo.UpsertMediaItemAsync(NewItem("first.mp4", "fp-first", folder.Id));
        var second = await Repo.UpsertMediaItemAsync(NewItem("second.mp4", "fp-second", folder.Id));

        await Repo.RecordPlaybackAsync(first.Id, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10));
        await Task.Delay(10); // ensure a distinct, later PlayedAt timestamp
        await Repo.RecordPlaybackAsync(second.Id, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(10));

        var recent = await Repo.GetRecentlyPlayedAsync();

        Assert.Equal(second.Id, recent[0].Id);
        Assert.Equal(first.Id, recent[1].Id);
    }

    [Fact]
    public async Task SearchAsync_matches_title_album_or_artist_case_insensitively()
    {
        var folder = await Repo.AddFolderAsync(Path.GetTempPath());
        await Repo.UpsertMediaItemAsync(NewItem("a.mp4", "fp-a", folder.Id, title: "Interstellar"));
        await Repo.UpsertMediaItemAsync(NewItem("b.mp4", "fp-b", folder.Id, title: "Inception"));

        var results = await Repo.SearchAsync("INTER");

        Assert.Single(results);
        Assert.Equal("Interstellar", results[0].Title);
    }

    [Fact]
    public async Task SearchAsync_with_empty_query_returns_no_items()
    {
        var folder = await Repo.AddFolderAsync(Path.GetTempPath());
        await Repo.UpsertMediaItemAsync(NewItem("a.mp4", "fp-empty-query", folder.Id, title: "Anything"));

        Assert.Empty(await Repo.SearchAsync(""));
    }

    [Fact]
    public async Task SetCoverArtPathAsync_persists_cover_art_path()
    {
        var folder = await Repo.AddFolderAsync(Path.GetTempPath());
        var item = await Repo.UpsertMediaItemAsync(NewItem("c.mp4", "fp-cover", folder.Id));
        Assert.Null(item.CoverArtPath);

        await Repo.SetCoverArtPathAsync(item.Id, @"C:\thumbs\c.jpg");

        var found = await Repo.FindByFingerprintAsync("fp-cover");
        Assert.Equal(@"C:\thumbs\c.jpg", found!.CoverArtPath);
    }

    [Fact]
    public async Task SetCoverArtPathAsync_clears_with_null()
    {
        var folder = await Repo.AddFolderAsync(Path.GetTempPath());
        var item = await Repo.UpsertMediaItemAsync(NewItem("d.mp4", "fp-cover-clear", folder.Id));
        await Repo.SetCoverArtPathAsync(item.Id, @"C:\thumbs\d.jpg");
        await Repo.SetCoverArtPathAsync(item.Id, null);

        var found = await Repo.FindByFingerprintAsync("fp-cover-clear");
        Assert.Null(found!.CoverArtPath);
    }

    private static LibraryMediaItem NewItem(string filePath, string fingerprint, int folderId, string? title = null) => new()
    {
        FilePath = filePath,
        Fingerprint = fingerprint,
        Title = title ?? filePath,
        LibraryFolderId = folderId,
    };
}
