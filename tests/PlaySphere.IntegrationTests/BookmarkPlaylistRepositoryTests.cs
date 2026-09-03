using PlaySphere.Application.Library;
using PlaySphere.Domain.Library;
using Xunit;

namespace PlaySphere.IntegrationTests;

/// <summary>
/// Integration tests for the bookmarks (PICKLE.md §21) and playlists (PICKLE.md §25) persistence
/// layer against a real migrated SQLite database, mirroring the media-library repository tests.
/// </summary>
public sealed class BookmarkPlaylistRepositoryTests : IDisposable
{
    private readonly LibraryDatabaseFixture _fixture = new();
    private IMediaLibraryRepository Repo => _fixture.Repository;

    private int _folderId;

    public void Dispose() => _fixture.Dispose();

    /// <summary>Creates a folder and a cataloged media item, returning the item id.</summary>
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
    public async Task AddBookmarkAsync_persists_label_note_and_category()
    {
        var mediaId = await NewMediaItemAsync("b1.mp4", "fp-bm-1");

        var bookmark = await Repo.AddBookmarkAsync(mediaId, 90_000, label: "Intro", note: "Cold open", category: "Scene");

        Assert.True(bookmark.Id > 0);
        Assert.Equal(mediaId, bookmark.MediaItemId);
        Assert.Equal(90_000, bookmark.PositionMs);
        Assert.Equal("Intro", bookmark.Label);
        Assert.Equal("Cold open", bookmark.Note);
        Assert.Equal("Scene", bookmark.Category);
        Assert.NotEqual(default, bookmark.CreatedAt);
    }

    [Fact]
    public async Task AddBookmarkAsync_clamps_negative_position_to_zero()
    {
        var mediaId = await NewMediaItemAsync("b2.mp4", "fp-bm-2");

        var bookmark = await Repo.AddBookmarkAsync(mediaId, -5000);

        Assert.Equal(0, bookmark.PositionMs);
    }

    [Fact]
    public async Task GetBookmarksAsync_orders_by_position_then_persists_between_commands()
    {
        var mediaId = await NewMediaItemAsync("b3.mp4", "fp-bm-3");

        var later = await Repo.AddBookmarkAsync(mediaId, 200_000, label: "Later");
        var earlier = await Repo.AddBookmarkAsync(mediaId, 10_000, label: "Earlier");

        var bookmarks = await Repo.GetBookmarksAsync(mediaId);

        Assert.Equal(2, bookmarks.Count);
        Assert.Equal(earlier.Id, bookmarks[0].Id);
        Assert.Equal(later.Id, bookmarks[1].Id);
    }

    [Fact]
    public async Task GetBookmarksAsync_returns_empty_for_item_without_bookmarks()
    {
        var mediaId = await NewMediaItemAsync("b4.mp4", "fp-bm-4");

        Assert.Empty(await Repo.GetBookmarksAsync(mediaId));
    }

    [Fact]
    public async Task RemoveBookmarkAsync_removes_only_that_bookmark()
    {
        var mediaId = await NewMediaItemAsync("b5.mp4", "fp-bm-5");
        var toKeep = await Repo.AddBookmarkAsync(mediaId, 10_000);
        var toRemove = await Repo.AddBookmarkAsync(mediaId, 20_000);

        await Repo.RemoveBookmarkAsync(toRemove.Id);

        var remaining = await Repo.GetBookmarksAsync(mediaId);
        var id = Assert.Single(remaining).Id;
        Assert.Equal(toKeep.Id, id);
    }

    [Fact]
    public async Task CreatePlaylistAsync_persists_trimmed_name()
    {
        var playlist = await Repo.CreatePlaylistAsync("  Road Trip  ");

        Assert.True(playlist.Id > 0);
        Assert.Equal("Road Trip", playlist.Name);
        Assert.NotEqual(default, playlist.CreatedAt);
    }

    [Fact]
    public async Task RenamePlaylistAsync_updates_name()
    {
        var playlist = await Repo.CreatePlaylistAsync("Old Name");

        await Repo.RenamePlaylistAsync(playlist.Id, "New Name");

        var playlists = await Repo.GetPlaylistsAsync();
        var updated = Assert.Single(playlists);
        Assert.Equal(playlist.Id, updated.Id);
        Assert.Equal("New Name", updated.Name);
    }

    [Fact]
    public async Task DeletePlaylistAsync_cascades_to_its_items_but_not_the_media()
    {
        var mediaId = await NewMediaItemAsync("p1.mp4", "fp-pl-1");
        var playlist = await Repo.CreatePlaylistAsync("To Delete");
        var item = await Repo.AddToPlaylistAsync(playlist.Id, mediaId);

        Assert.Single(await Repo.GetPlaylistItemsAsync(playlist.Id));

        await Repo.DeletePlaylistAsync(playlist.Id);

        Assert.Empty(await Repo.GetPlaylistsAsync());
        Assert.Empty(await Repo.GetPlaylistItemsAsync(playlist.Id));
        Assert.NotNull(await Repo.FindByFingerprintAsync("fp-pl-1"));
    }

    [Fact]
    public async Task GetPlaylistsAsync_orders_by_creation_oldest_first()
    {
        var first = await Repo.CreatePlaylistAsync("First");
        var second = await Repo.CreatePlaylistAsync("Second");

        var playlists = await Repo.GetPlaylistsAsync();

        Assert.Equal(first.Id, playlists[0].Id);
        Assert.Equal(second.Id, playlists[1].Id);
    }

    [Fact]
    public async Task AddToPlaylistAsync_appends_at_increasing_position()
    {
        var mediaA = await NewMediaItemAsync("a.mp4", "fp-add-a");
        var mediaB = await NewMediaItemAsync("b.mp4", "fp-add-b");
        var playlist = await Repo.CreatePlaylistAsync("Growing");

        var first = await Repo.AddToPlaylistAsync(playlist.Id, mediaA);
        var second = await Repo.AddToPlaylistAsync(playlist.Id, mediaB);

        Assert.Equal(0, first.Position);
        Assert.Equal(1, second.Position);
    }

    [Fact]
    public async Task AddRangeToPlaylistAsync_preserves_order_and_appends_after_existing()
    {
        var mediaA = await NewMediaItemAsync("a.mp4", "fp-range-a");
        var mediaB = await NewMediaItemAsync("b.mp4", "fp-range-b");
        var mediaC = await NewMediaItemAsync("c.mp4", "fp-range-c");
        var playlist = await Repo.CreatePlaylistAsync("Range");

        await Repo.AddToPlaylistAsync(playlist.Id, mediaA);
        await Repo.AddRangeToPlaylistAsync(playlist.Id, new[] { mediaB, mediaC });

        var items = await Repo.GetPlaylistItemsAsync(playlist.Id);
        Assert.Equal(3, items.Count);
        Assert.Equal(mediaA, items[0].MediaItemId);
        Assert.Equal(mediaB, items[1].MediaItemId);
        Assert.Equal(mediaC, items[2].MediaItemId);
        Assert.Equal(new[] { 0, 1, 2 }, items.Select(i => i.Position).ToArray());
    }

    [Fact]
    public async Task RemoveFromPlaylistAsync_removes_only_the_target_row()
    {
        var mediaA = await NewMediaItemAsync("a.mp4", "fp-rm-a");
        var mediaB = await NewMediaItemAsync("b.mp4", "fp-rm-b");
        var playlist = await Repo.CreatePlaylistAsync("Remove Row");

        var first = await Repo.AddToPlaylistAsync(playlist.Id, mediaA);
        await Repo.AddToPlaylistAsync(playlist.Id, mediaB);

        await Repo.RemoveFromPlaylistAsync(first.Id);

        var items = await Repo.GetPlaylistItemsAsync(playlist.Id);
        var id = Assert.Single(items).Id;
        Assert.Equal(first.Id + 1, id); // the second-inserted row survives
    }

    [Fact]
    public async Task ReorderPlaylistAsync_rewrites_position_and_drops_omitted_rows()
    {
        var mediaA = await NewMediaItemAsync("a.mp4", "fp-reorder-a");
        var mediaB = await NewMediaItemAsync("b.mp4", "fp-reorder-b");
        var mediaC = await NewMediaItemAsync("c.mp4", "fp-reorder-c");
        var playlist = await Repo.CreatePlaylistAsync("Reorder");

        await Repo.AddRangeToPlaylistAsync(playlist.Id, new[] { mediaA, mediaB, mediaC });

        // New order: C, A — omitting B entirely (full replacement of order).
        await Repo.ReorderPlaylistAsync(playlist.Id, new[] { mediaC, mediaA });

        var items = await Repo.GetPlaylistItemsAsync(playlist.Id);
        Assert.Equal(2, items.Count);
        Assert.Equal(new[] { mediaC, mediaA }, items.Select(i => i.MediaItemId).ToArray());
        Assert.Equal(new[] { 0, 1 }, items.Select(i => i.Position).ToArray());
    }

    [Fact]
    public async Task GetPlaylistItemsAsync_returns_items_in_position_order()
    {
        var mediaA = await NewMediaItemAsync("a.mp4", "fp-gpi-a");
        var mediaB = await NewMediaItemAsync("b.mp4", "fp-gpi-b");
        var playlist = await Repo.CreatePlaylistAsync("Ordered");

        // Insert B first then A; the persisted positions must still yield A, B in order.
        await Repo.AddToPlaylistAsync(playlist.Id, mediaB);
        await Repo.AddToPlaylistAsync(playlist.Id, mediaA);

        var items = await Repo.GetPlaylistItemsAsync(playlist.Id);
        Assert.Equal(new[] { mediaB, mediaA }, items.Select(i => i.MediaItemId).ToArray());
    }
}
