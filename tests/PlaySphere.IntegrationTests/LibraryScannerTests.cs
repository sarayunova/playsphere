using Microsoft.Extensions.Logging.Abstractions;
using PlaySphere.Library;
using PlaySphere.Media.Probe;
using Xunit;

namespace PlaySphere.IntegrationTests;

/// <summary>
/// Tests for <see cref="LibraryScanner"/> against a real temp directory of files and a real
/// migrated SQLite database (PICKLE.md §22 "scanning") — no mocks.
/// </summary>
public sealed class LibraryScannerTests : IDisposable
{
    private readonly LibraryDatabaseFixture _fixture = new();
    private readonly string _mediaDirectory;
    private readonly LibraryScanner _scanner;

    public LibraryScannerTests()
    {
        _mediaDirectory = Path.Combine(Path.GetTempPath(), "PlaySphereScannerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_mediaDirectory);
        _scanner = new LibraryScanner(_fixture.Repository, NullLogger<LibraryScanner>.Instance);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        if (Directory.Exists(_mediaDirectory))
        {
            Directory.Delete(_mediaDirectory, recursive: true);
        }
    }

    private string WriteMediaFile(string relativePath, byte[]? content = null)
    {
        var path = Path.Combine(_mediaDirectory, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content ?? [1, 2, 3, 4]);
        return path;
    }

    [Fact]
    public async Task ScanFolderAsync_catalogs_recognized_media_files()
    {
        WriteMediaFile("movie.mp4");
        // Distinct content so the two files get distinct fingerprints (content is part of media
        // identity — two same-byte, same-timestamp files would otherwise dedup into one item).
        WriteMediaFile("song.mp3", [9, 9, 9, 9]);
        var folder = await _fixture.Repository.AddFolderAsync(_mediaDirectory);

        var result = await _scanner.ScanFolderAsync(folder);

        Assert.Equal(2, result.Added);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Failed);
        Assert.Equal(2, (await _fixture.Repository.GetAllMediaItemsAsync()).Count);
    }

    [Fact]
    public async Task ScanFolderAsync_ignores_non_media_extensions()
    {
        WriteMediaFile("movie.mp4");
        WriteMediaFile("poster.jpg");
        WriteMediaFile("readme.txt");
        var folder = await _fixture.Repository.AddFolderAsync(_mediaDirectory);

        var result = await _scanner.ScanFolderAsync(folder);

        Assert.Equal(1, result.Added);
    }

    [Fact]
    public async Task ScanFolderAsync_recurses_into_subdirectories()
    {
        WriteMediaFile(Path.Combine("Season 1", "episode1.mkv"));
        // Distinct content to keep the two episodes' fingerprints distinct (see catalogs test).
        WriteMediaFile(Path.Combine("Season 1", "episode2.mkv"), [7, 7, 7, 7]);
        var folder = await _fixture.Repository.AddFolderAsync(_mediaDirectory);

        var result = await _scanner.ScanFolderAsync(folder);

        Assert.Equal(2, result.Added);
    }

    [Fact]
    public async Task ScanFolderAsync_second_run_updates_rather_than_duplicates()
    {
        WriteMediaFile("movie.mp4");
        var folder = await _fixture.Repository.AddFolderAsync(_mediaDirectory);

        await _scanner.ScanFolderAsync(folder);
        var second = await _scanner.ScanFolderAsync(folder);

        Assert.Equal(0, second.Added);
        Assert.Equal(1, second.Updated);
        Assert.Single(await _fixture.Repository.GetAllMediaItemsAsync());
    }

    [Fact]
    public async Task ScanFolderAsync_titles_items_from_filename()
    {
        WriteMediaFile("My Great Movie.mp4");
        var folder = await _fixture.Repository.AddFolderAsync(_mediaDirectory);

        await _scanner.ScanFolderAsync(folder);

        var items = await _fixture.Repository.GetAllMediaItemsAsync();
        Assert.Equal("My Great Movie", items[0].Title);
    }

    [Fact]
    public async Task ScanFolderAsync_on_folder_with_no_media_returns_zero_counts()
    {
        WriteMediaFile("notes.txt");
        var folder = await _fixture.Repository.AddFolderAsync(_mediaDirectory);

        var result = await _scanner.ScanFolderAsync(folder);

        Assert.Equal(0, result.Added);
        Assert.Equal(0, result.Updated);
        Assert.Equal(0, result.Failed);
    }

    [Fact]
    public async Task ScanFolderAsync_populates_technical_metadata_when_a_probe_is_available()
    {
        if (MediaTestTools.FfProbe is null)
        {
            return;
        }
        var sample = await MediaTestTools.GenerateSampleVideoAsync(Path.Combine(_mediaDirectory, "sample.mp4"));
        var folder = await _fixture.Repository.AddFolderAsync(_mediaDirectory);
        var probe = new FfProbeMediaProbe(NullLogger<FfProbeMediaProbe>.Instance, new FfProbeOptions { ExecutablePath = MediaTestTools.FfProbe });
        var scannerWithProbe = new LibraryScanner(_fixture.Repository, NullLogger<LibraryScanner>.Instance, probe);

        var result = await scannerWithProbe.ScanFolderAsync(folder);

        Assert.Equal(1, result.Added);
        var item = (await _fixture.Repository.GetAllMediaItemsAsync()).Single();
        Assert.InRange(item.Duration.TotalSeconds, 1.5, 2.5);
        Assert.Equal("320x180", item.Resolution);
        Assert.Equal("h264", item.Codec);
        Assert.NotNull(item.FrameRate);
        Assert.Equal(1, item.AudioTrackCount);
    }
}
