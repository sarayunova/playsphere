using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using PlaySphere.Application.Diagnostics;
using PlaySphere.Infrastructure.Diagnostics;
using Xunit;

namespace PlaySphere.IntegrationTests;

/// <summary>
/// Zip packing against a real temp folder: present logs are included, missing logs are skipped,
/// and media / preferences / the library database stay out unless the database is explicitly opted in.
/// </summary>
public sealed class DiagnosticPackTests : IDisposable
{
    private const string ApiKey = "sk-proj-ABCDEFGHIJKLMNOPQRSTUV";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ps-diag-" + Guid.NewGuid().ToString("N"));

    public DiagnosticPackTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A zip still open in a failed assertion. The OS reclaims the temp folder.
        }
    }

    [Fact]
    public async Task Pack_includes_existing_logs_skips_missing_ones_and_leaves_media_out()
    {
        var data = NewDataDirectory();
        File.WriteAllText(Path.Combine(data, "crash.log"), "boom " + ApiKey);
        File.WriteAllText(Path.Combine(data, "crash.log.old"), "older");
        File.WriteAllText(Path.Combine(data, "movie.mp4"), "VIDEO");
        File.WriteAllText(Path.Combine(data, "preferences.json"), "PREF-SECRET");
        File.WriteAllText(Path.Combine(data, "report.txt"), "PLANTED-REPORT");
        File.WriteAllText(Path.Combine(data, "library.db"), "SQLITE-TEST");
        Directory.CreateDirectory(Path.Combine(data, "thumbs"));
        File.WriteAllText(Path.Combine(data, "thumbs", "cover.jpg"), "JPEG");

        var result = await Service(data).CreatePackAsync(new DiagnosticPackRequest
        {
            DataDirectory = data,
            Description = "The picture froze",
            Contact = "tester@example.com",
            IncludeLibraryDatabase = false,
            CreatedAtUtc = new DateTimeOffset(2026, 10, 23, 0, 39, 0, TimeSpan.Zero),
            AppVersion = "0.10.0",
            OsDescription = "Test OS",
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.NotNull(result.ZipPath);
        Assert.Equal(
            ["report.txt", "crash.log", "crash.log.old"],
            result.IncludedEntries);
        Assert.Contains(result.Notes, note => note.Contains("libvlc.log not found", StringComparison.Ordinal));
        Assert.Contains(result.Notes, note => note.Contains("visualizer-audio.log not found", StringComparison.Ordinal));

        var entries = ReadEntries(result.ZipPath!);
        Assert.Equal(["crash.log", "crash.log.old", "report.txt"], entries.Keys.OrderBy(name => name, StringComparer.Ordinal));
        Assert.Contains("sk-[redacted]", entries["crash.log"], StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, entries["crash.log"], StringComparison.Ordinal);
        Assert.Equal("older", entries["crash.log.old"]);
        Assert.Contains("The picture froze", entries["report.txt"], StringComparison.Ordinal);
        Assert.Contains("tester@example.com", entries["report.txt"], StringComparison.Ordinal);
        Assert.Contains("2026-10-23 00:39:00 UTC", entries["report.txt"], StringComparison.Ordinal);
        Assert.Contains("Library database: not included.", entries["report.txt"], StringComparison.Ordinal);
        Assert.DoesNotContain("PLANTED-REPORT", entries["report.txt"], StringComparison.Ordinal);
        Assert.DoesNotContain("PREF-SECRET", string.Join('\n', entries.Values), StringComparison.Ordinal);
        Assert.DoesNotContain("VIDEO", string.Join('\n', entries.Values), StringComparison.Ordinal);
        Assert.DoesNotContain("SQLITE-TEST", string.Join('\n', entries.Values), StringComparison.Ordinal);
        Assert.False(File.Exists(result.ZipPath + ".partial"));
    }

    [Fact]
    public async Task Pack_includes_the_library_database_only_when_opted_in()
    {
        var data = NewDataDirectory();
        File.WriteAllText(Path.Combine(data, "library.db"), "SQLITE-TEST");
        File.WriteAllText(Path.Combine(data, "library.db-wal"), "WAL");
        File.WriteAllText(Path.Combine(data, "library.db-journal"), "JOURNAL");
        File.WriteAllText(Path.Combine(data, "clip.mkv"), "MEDIA");

        var result = await Service(data).CreatePackAsync(new DiagnosticPackRequest
        {
            DataDirectory = data,
            IncludeLibraryDatabase = true,
            Description = "Please look at the library",
        });

        Assert.True(result.Success, result.ErrorMessage);
        var entries = ReadEntries(result.ZipPath!);
        Assert.Equal("SQLITE-TEST", entries["library.db"]);
        Assert.Equal("WAL", entries["library.db-wal"]);
        Assert.False(entries.ContainsKey("library.db-journal"));
        Assert.False(entries.ContainsKey("clip.mkv"));
        Assert.Contains("Library database: included (library.db, library.db-wal).", entries["report.txt"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pack_with_no_logs_still_writes_the_tester_note()
    {
        var data = NewDataDirectory();
        var result = await Service(data).CreatePackAsync(new DiagnosticPackRequest
        {
            DataDirectory = data,
            Description = "café — no logs yet",
        });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.Equal(["report.txt"], result.IncludedEntries);
        var entries = ReadEntries(result.ZipPath!);
        Assert.Contains("café — no logs yet", entries["report.txt"], StringComparison.Ordinal);
        Assert.Contains("crash.log not found", entries["report.txt"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pack_tails_an_oversized_log_and_skips_an_oversized_database()
    {
        var data = NewDataDirectory();
        File.WriteAllText(
            Path.Combine(data, "crash.log"),
            "HEAD_MARKER" + new string('x', 400) + "TAIL_MARKER");
        File.WriteAllText(Path.Combine(data, "library.db"), new string('D', 300));

        var result = await Service(data, maxLogBytes: 64, maxLibraryBytes: 100).CreatePackAsync(
            new DiagnosticPackRequest
            {
                DataDirectory = data,
                IncludeLibraryDatabase = true,
            });

        Assert.True(result.Success, result.ErrorMessage);
        var entries = ReadEntries(result.ZipPath!);
        Assert.Contains("TAIL_MARKER", entries["crash.log"], StringComparison.Ordinal);
        Assert.DoesNotContain("HEAD_MARKER", entries["crash.log"], StringComparison.Ordinal);
        Assert.Contains("[truncated to the last 64 bytes]", entries["crash.log"], StringComparison.Ordinal);
        Assert.False(entries.ContainsKey("library.db"));
        Assert.Contains(result.Notes, note => note.Contains("library.db was skipped", StringComparison.Ordinal));
        Assert.Contains("Library database: requested, but it was not included.", entries["report.txt"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pack_skips_a_directory_that_uses_a_log_name_and_still_packs_other_logs()
    {
        var data = NewDataDirectory();
        Directory.CreateDirectory(Path.Combine(data, "crash.log"));
        File.WriteAllText(Path.Combine(data, "libvlc.log"), "native");

        var result = await Service(data).CreatePackAsync(new DiagnosticPackRequest { DataDirectory = data });

        Assert.True(result.Success, result.ErrorMessage);
        var entries = ReadEntries(result.ZipPath!);
        Assert.False(entries.ContainsKey("crash.log"));
        Assert.Equal("native", entries["libvlc.log"]);
        Assert.Contains(result.Notes, note => note.Contains("crash.log not found", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pack_does_not_follow_a_symlink_out_of_the_data_folder()
    {
        var data = NewDataDirectory();
        var outside = Path.Combine(_root, "outside-secret.txt");
        File.WriteAllText(outside, "SECRET-OUTSIDE");
        var link = Path.Combine(data, "crash.log");
        try
        {
            File.CreateSymbolicLink(link, outside);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // GitHub-hosted Windows runners often cannot create symlinks. The allow-list
            // tests already prove arbitrary files are not packed.
            return;
        }

        var result = await Service(data).CreatePackAsync(new DiagnosticPackRequest { DataDirectory = data });

        Assert.True(result.Success, result.ErrorMessage);
        var entries = ReadEntries(result.ZipPath!);
        Assert.False(entries.ContainsKey("crash.log"));
        Assert.DoesNotContain("SECRET-OUTSIDE", string.Join('\n', entries.Values), StringComparison.Ordinal);
        Assert.Contains(result.Notes, note => note.Contains("not a regular file", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Pack_fails_cleanly_when_the_reports_folder_cannot_be_created()
    {
        var data = NewDataDirectory();
        File.WriteAllText(Path.Combine(data, "reports"), "not a directory");
        File.WriteAllText(Path.Combine(data, "crash.log"), "still here");

        var result = await Service(data).CreatePackAsync(new DiagnosticPackRequest { DataDirectory = data });

        Assert.False(result.Success);
        Assert.Null(result.ZipPath);
        Assert.Contains("reports folder", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("still here", File.ReadAllText(Path.Combine(data, "crash.log")));
    }

    [Fact]
    public async Task Pack_deletes_expired_zips_and_keeps_logs()
    {
        var data = NewDataDirectory();
        var reports = Path.Combine(data, "reports");
        Directory.CreateDirectory(reports);
        var oldZip = Path.Combine(reports, "PlaySphere-diagnostics-old.zip");
        var recentZip = Path.Combine(reports, "PlaySphere-diagnostics-recent.zip");
        File.WriteAllText(oldZip, "old");
        File.WriteAllText(recentZip, "recent");
        File.SetLastWriteTimeUtc(oldZip, DateTime.UtcNow.AddDays(-30));
        File.SetLastWriteTimeUtc(recentZip, DateTime.UtcNow.AddDays(-1));
        var log = Path.Combine(data, "crash.log");
        File.WriteAllText(log, "keep");
        File.SetLastWriteTimeUtc(log, DateTime.UtcNow.AddDays(-30));

        var result = await Service(data).CreatePackAsync(new DiagnosticPackRequest { DataDirectory = data });

        Assert.True(result.Success, result.ErrorMessage);
        Assert.False(File.Exists(oldZip));
        Assert.True(File.Exists(recentZip));
        Assert.Equal("keep", File.ReadAllText(log));
        Assert.NotEqual(oldZip, result.ZipPath);
        Assert.NotEqual(recentZip, result.ZipPath);
    }

    [Fact]
    public async Task Inspect_reports_which_catalog_files_exist()
    {
        var data = NewDataDirectory();
        File.WriteAllText(Path.Combine(data, "crash.log"), "hi");

        var inventory = Service(data).Inspect(data);

        Assert.Equal(Path.GetFullPath(data), inventory.DataDirectory);
        var crash = Assert.Single(inventory.Files, file => file.FileName == "crash.log");
        Assert.True(crash.Exists);
        Assert.Equal(2, crash.LengthBytes);
        var libvlc = Assert.Single(inventory.Files, file => file.FileName == "libvlc.log");
        Assert.False(libvlc.Exists);
        Assert.Contains(inventory.Files, file => file.FileName == "library.db" && !file.Exists);
    }

    [Fact]
    public void AddPlaySphereDiagnostics_registers_the_packer_and_options()
    {
        var services = new ServiceCollection();
        services.AddPlaySphereDiagnostics(new DiagnosticReportOptions
        {
            SupportEmail = "dev@livisoftware.example",
            DataDirectory = _root,
        });

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IDiagnosticReportService>();
        var options = provider.GetRequiredService<DiagnosticReportOptions>();

        Assert.IsType<ZipDiagnosticReportService>(service);
        Assert.Equal("dev@livisoftware.example", options.SupportEmail);
        Assert.Equal(Path.GetFullPath(_root), service.Inspect(null).DataDirectory);
    }

    private string NewDataDirectory()
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static ZipDiagnosticReportService Service(string dataDirectory, int maxLogBytes = 0, int maxLibraryBytes = 0) =>
        new(new DiagnosticReportOptions
        {
            DataDirectory = dataDirectory,
            SupportEmail = DiagnosticReportDefaults.SupportEmail,
            MaxLogBytes = maxLogBytes,
            MaxLibraryDatabaseBytes = maxLibraryBytes,
        });

    private static Dictionary<string, string> ReadEntries(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            entries[entry.FullName] = reader.ReadToEnd();
        }

        return entries;
    }
}
