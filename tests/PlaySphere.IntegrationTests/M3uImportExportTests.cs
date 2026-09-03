using Microsoft.Extensions.Logging.Abstractions;
using PlaySphere.Application.Library;
using PlaySphere.Domain.Library;
using PlaySphere.Library;
using Xunit;

namespace PlaySphere.IntegrationTests;

/// <summary>
/// Integration tests for M3U/M3U8 import and export (PICKLE.md §25) against a real migrated SQLite
/// database. Writes real temp media files and real .m3u files, exercising the full
/// parse → catalog → playlist pipeline.
/// </summary>
public sealed class M3uImportExportTests : IDisposable
{
    private readonly LibraryDatabaseFixture _fixture = new();
    private readonly string _tempDir;

    public M3uImportExportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PlaySphereM3uTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    private IM3uImporter Importer => new M3uImporter(_fixture.Repository, NullLogger<M3uImporter>.Instance);
    private IM3uExporter Exporter => new M3uExporter(NullLogger<M3uExporter>.Instance);
    private IMediaLibraryRepository Repo => _fixture.Repository;

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best-effort */ }
        }
    }

    private string NewMediaFile(string name, byte[]? content = null)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllBytes(path, content ?? System.Text.Encoding.UTF8.GetBytes(name));
        return path;
    }

    private async Task<int> AddCatalogItemAsync(string path)
    {
        var folderId = (await Repo.AddFolderAsync(_tempDir)).Id;
        var fingerprint = MediaFileFingerprinter.Compute(path);
        var item = await Repo.UpsertMediaItemAsync(new LibraryMediaItem
        {
            FilePath = path,
            Fingerprint = fingerprint,
            LibraryFolderId = folderId,
        });
        return item.Id;
    }

    [Fact]
    public async Task ImportAsync_catalogs_supported_files_and_creates_playlist_in_order()
    {
        var a = NewMediaFile("a.mp4");
        var b = NewMediaFile("b.mkv");
        var m3u = Path.Combine(_tempDir, "list.m3u");
        File.WriteAllText(m3u, $"#EXTM3U\n{a}\n#EXTINF:10,Second\n{b}\n");

        var result = await Importer.ImportAsync(m3u);

        Assert.Equal("list", result.Playlist.Name);
        Assert.Equal(2, result.Imported);
        Assert.Equal(0, result.Skipped);

        var aId = (await Repo.FindByPathAsync(a))!.Id;
        var bId = (await Repo.FindByPathAsync(b))!.Id;
        var items = await Repo.GetPlaylistItemsAsync(result.Playlist.Id);
        Assert.Equal(2, items.Count);
        Assert.Equal(aId, items[0].MediaItemId);
        Assert.Equal(bId, items[1].MediaItemId);
    }

    [Fact]
    public async Task ImportAsync_skips_missing_and_unsupported_entries()
    {
        var a = NewMediaFile("a.mp4");
        var missing = Path.Combine(_tempDir, "does-not-exist.mp4");
        var unsupported = NewMediaFile("readme.txt");
        var m3u = Path.Combine(_tempDir, "list.m3u");
        File.WriteAllText(m3u, $"{missing}\n{a}\n{unsupported}\n");

        var result = await Importer.ImportAsync(m3u);

        Assert.Equal(1, result.Imported);
        Assert.Equal(2, result.Skipped);
        var aId = (await Repo.FindByPathAsync(a))!.Id;
        var items = await Repo.GetPlaylistItemsAsync(result.Playlist.Id);
        Assert.Equal(aId, Assert.Single(items).MediaItemId);
    }

    [Fact]
    public async Task ImportAsync_uses_provided_playlist_name()
    {
        var a = NewMediaFile("a.mp4");
        var m3u = Path.Combine(_tempDir, "list.m3u");
        File.WriteAllText(m3u, $"{a}\n");

        var result = await Importer.ImportAsync(m3u, "My Favorites");

        Assert.Equal("My Favorites", result.Playlist.Name);
    }

    [Fact]
    public async Task ImportAsync_reuses_existing_catalog_item_by_fingerprint()
    {
        var a = NewMediaFile("a.mp4");
        var catalogedId = await AddCatalogItemAsync(a);
        var m3u = Path.Combine(_tempDir, "list.m3u");
        File.WriteAllText(m3u, $"{a}\n");

        var result = await Importer.ImportAsync(m3u);

        Assert.Equal(1, result.Imported);
        var items = await Repo.GetPlaylistItemsAsync(result.Playlist.Id);
        Assert.Equal(catalogedId, items[0].MediaItemId);
    }

    [Fact]
    public async Task ExportAsync_writes_extm3u_and_roundtrips()
    {
        var a = NewMediaFile("a.mp4");
        var b = NewMediaFile("b.mkv");
        var outFile = Path.Combine(_tempDir, "export.m3u");

        var ok = await Exporter.ExportAsync(new[] { a, b }, outFile);

        Assert.True(ok);
        var content = File.ReadAllText(outFile);
        Assert.StartsWith("#EXTM3U", content);
        Assert.Contains(a, content);
        Assert.Contains(b, content);

        // Round-trip: importer can now consume the exported file.
        var result = await Importer.ImportAsync(outFile);
        Assert.Equal(2, result.Imported);
    }

    [Fact]
    public async Task ExportAsync_returns_false_when_destination_is_invalid()
    {
        // A path whose parent is a file (not a directory) cannot be created.
        var blocker = NewMediaFile("blocker.mp4");
        var badDestination = Path.Combine(blocker, "sub", "out.m3u");

        var ok = await Exporter.ExportAsync(new[] { blocker }, badDestination);

        Assert.False(ok);
    }
}
