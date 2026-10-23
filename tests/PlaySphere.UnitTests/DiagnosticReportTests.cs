using PlaySphere.Application.Diagnostics;
using Xunit;

namespace PlaySphere.UnitTests;

public sealed class DiagnosticReportTests
{
    private const string ApiKey = "sk-proj-ABCDEFGHIJKLMNOPQRSTUV";

    [Fact]
    public void Catalog_lists_the_logs_the_app_actually_writes()
    {
        Assert.Equal(
            [
                DiagnosticLogCatalog.CrashLogFileName,
                DiagnosticLogCatalog.CrashLogRotationFileName,
                DiagnosticLogCatalog.LibVlcLogFileName,
                DiagnosticLogCatalog.VisualizerAudioLogFileName,
            ],
            DiagnosticLogCatalog.LogFiles.Select(file => file.FileName));
        Assert.Equal(
            ["library.db", "library.db-wal", "library.db-shm"],
            DiagnosticLogCatalog.LibraryDatabaseFiles.Select(file => file.FileName));
        Assert.Equal("LiVi PlaySphere Studio", DiagnosticReportLocations.DataFolderName);
    }

    [Fact]
    public void Select_keeps_present_logs_and_drops_everything_else_by_default()
    {
        var present = new[]
        {
            "crash.log",
            "movie.mp4",
            "preferences.json",
            "report.txt",
            "library.db",
            "library.db-journal",
            "library.db-wal",
            @"..\crash.log",
            "thumb.jpg",
            "visualizer-audio.log",
            "notes/crash.log",
        };

        var selected = DiagnosticLogCatalog.SelectPackFileNames(present, includeLibraryDatabase: false);

        Assert.Equal(["crash.log", "visualizer-audio.log"], selected);
    }

    [Fact]
    public void Select_adds_library_sidecars_only_when_opted_in()
    {
        var present = new[] { "crash.log", "library.db", "library.db-wal", "library.db-shm", "library.db-journal", "song.mp3" };

        var selected = DiagnosticLogCatalog.SelectPackFileNames(present, includeLibraryDatabase: true);

        Assert.Equal(["crash.log", "library.db", "library.db-wal", "library.db-shm"], selected);
    }

    [Fact]
    public void Select_skips_missing_catalog_files_and_unsafe_names()
    {
        var selected = DiagnosticLogCatalog.SelectPackFileNames(["libvlc.log"], includeLibraryDatabase: true);

        Assert.Equal(["libvlc.log"], selected);
        Assert.False(DiagnosticLogCatalog.IsSafeFileName(@"..\crash.log"));
        Assert.False(DiagnosticLogCatalog.IsSafeFileName("nested/crash.log"));
        Assert.False(DiagnosticLogCatalog.IsSafeFileName("C:crash.log"));
        Assert.False(DiagnosticLogCatalog.IsSafeFileName(""));
        Assert.False(DiagnosticLogCatalog.IsSafeFileName(null));
    }

    [Fact]
    public void Report_text_redacts_secrets_and_states_the_privacy_boundary()
    {
        var text = DiagnosticReportText.BuildReport(new DiagnosticReportDocument
        {
            CreatedAtUtc = new DateTimeOffset(2026, 10, 23, 0, 39, 0, TimeSpan.Zero),
            AppVersion = "0.10.0",
            OsDescription = "Test OS",
            Description = "Failed after " + ApiKey,
            Contact = "me@example.com",
            LibraryDatabaseRequested = false,
            IncludedFiles = ["report.txt", "crash.log"],
            MissingLogs = ["libvlc.log"],
            Notes = ["crash.log.old not found"],
        });

        Assert.Contains("2026-10-23 00:39:00 UTC", text, StringComparison.Ordinal);
        Assert.Contains("App version: 0.10.0", text, StringComparison.Ordinal);
        Assert.Contains("sk-[redacted]", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ABCDEFGHIJKLMNOPQRSTUV", text, StringComparison.Ordinal);
        Assert.Contains("Reply contact: me@example.com", text, StringComparison.Ordinal);
        Assert.Contains("Library database: not included.", text, StringComparison.Ordinal);
        Assert.Contains("Media files, thumbnails, preferences.json, and source media are not included.", text, StringComparison.Ordinal);
        Assert.Contains("PlaySphere does not upload it.", text, StringComparison.Ordinal);
        Assert.Contains("- libvlc.log", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_text_names_an_opted_in_library_database()
    {
        var text = DiagnosticReportText.BuildReport(new DiagnosticReportDocument
        {
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
            LibraryDatabaseRequested = true,
            IncludedFiles = ["report.txt", "library.db"],
        });

        Assert.Contains("Library database: included (library.db).", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Report_text_says_when_an_opted_in_database_was_missing()
    {
        var text = DiagnosticReportText.BuildReport(new DiagnosticReportDocument
        {
            CreatedAtUtc = DateTimeOffset.UnixEpoch,
            LibraryDatabaseRequested = true,
            IncludedFiles = ["report.txt"],
        });

        Assert.Contains("Library database: requested, but it was not included.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Confirmation_lists_which_logs_will_be_included_and_which_are_missing()
    {
        var inventory = new DiagnosticReportInventory(
            @"C:\data",
            [
                new DiagnosticFileStatus("crash.log", "errors", DiagnosticFileKind.Log, true, 2048),
                new DiagnosticFileStatus("libvlc.log", "native", DiagnosticFileKind.Log, false, 0),
                new DiagnosticFileStatus("library.db", "db", DiagnosticFileKind.LibraryDatabase, true, 80),
            ]);

        var off = DiagnosticReportText.BuildConfirmation(inventory, includeLibraryDatabase: false, DiagnosticReportDefaults.SupportEmail);
        Assert.Contains("crash.log — will include", off, StringComparison.Ordinal);
        Assert.Contains("libvlc.log — not found, will skip", off, StringComparison.Ordinal);
        Assert.Contains("Library database — not included", off, StringComparison.Ordinal);
        Assert.Contains("left out unless you check the box", off, StringComparison.Ordinal);
        Assert.Contains("never included", off, StringComparison.Ordinal);
        Assert.Contains(DiagnosticReportDefaults.SupportEmail, off, StringComparison.Ordinal);
        Assert.DoesNotContain("placeholder", off, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Nothing is uploaded", off, StringComparison.Ordinal);

        var on = DiagnosticReportText.FormatFileList(inventory, includeLibraryDatabase: true);
        Assert.Contains("Library database — will include if the file is present", on, StringComparison.Ordinal);
        Assert.Contains("library.db — will include", on, StringComparison.Ordinal);
    }

    [Fact]
    public void Mailto_encodes_the_body_and_does_not_put_the_contact_in_the_to_address()
    {
        var uri = DiagnosticMailto.TryCreateUri(
            DiagnosticReportDefaults.SupportEmail,
            @"C:\Users\Tester\PlaySphere-diagnostics.zip",
            "Line one\r\nLine two",
            "me@example.com",
            ["report.txt", "crash.log"],
            libraryDatabaseRequested: false);

        Assert.NotNull(uri);
        Assert.StartsWith("mailto:" + DiagnosticReportDefaults.SupportEmail + "?", uri, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", uri, StringComparison.Ordinal);
        Assert.DoesNotContain("\r", uri, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("Line one"), uri, StringComparison.Ordinal);
        Assert.Contains("subject=", uri, StringComparison.Ordinal);
        var to = uri[..uri.IndexOf('?', StringComparison.Ordinal)];
        Assert.DoesNotContain("me@example.com", to, StringComparison.Ordinal);
        Assert.Contains(Uri.EscapeDataString("me@example.com"), uri, StringComparison.Ordinal);
        Assert.True(uri.Length <= DiagnosticMailto.MaxUriLength);
    }

    [Fact]
    public void Mailto_rejects_header_injection_and_shrinks_a_long_note()
    {
        Assert.Null(DiagnosticMailto.TryCreateUri(
            "owner@livisoftware.example\r\nBcc:evil@example.com",
            "zip.zip",
            "hi",
            null,
            ["crash.log"],
            false));
        Assert.Null(DiagnosticMailto.TryCreateUri("not-an-email", "zip.zip", "hi", null, [], false));
        Assert.Null(DiagnosticMailto.TryCreateUri("", "zip.zip", "hi", null, [], false));

        var uri = DiagnosticMailto.TryCreateUri(
            "dev@livisoftware.example",
            @"C:\very\long\path\PlaySphere-diagnostics.zip",
            ApiKey + new string('x', 5000),
            "me@example.com",
            ["crash.log"],
            false);

        Assert.NotNull(uri);
        Assert.True(uri.Length <= DiagnosticMailto.MaxUriLength);
        Assert.DoesNotContain(ApiKey, Uri.UnescapeDataString(uri), StringComparison.Ordinal);
        Assert.Contains("sk-[redacted]", Uri.UnescapeDataString(uri), StringComparison.Ordinal);
    }

    [Fact]
    public void Default_support_address_is_the_liviverse_mailbox()
    {
        Assert.Equal("contact@liviverse.com", DiagnosticReportDefaults.SupportEmail);
        Assert.True(DiagnosticReportDefaults.IsPlausibleSupportEmail(DiagnosticReportDefaults.SupportEmail));
        Assert.False(DiagnosticReportDefaults.IsPlaceholderAddress(DiagnosticReportDefaults.SupportEmail));
        Assert.True(DiagnosticReportDefaults.IsPlaceholderAddress("beta-feedback@livisoftware.example"));
        Assert.False(DiagnosticReportDefaults.IsPlaceholderAddress("dev@livisoftware.com"));
        Assert.False(DiagnosticReportDefaults.IsPlausibleSupportEmail("a@b"));
        Assert.Null(DiagnosticReportText.PlaceholderWarning(DiagnosticReportDefaults.SupportEmail));
        Assert.NotNull(DiagnosticReportText.PlaceholderWarning("beta-feedback@livisoftware.example"));
        Assert.Null(DiagnosticReportText.PlaceholderWarning("dev@liviverse.com"));
    }

    [Fact]
    public void Redactor_covers_openai_keys_and_bearer_tokens()
    {
        var text = DiagnosticSecretRedactor.Redact("key " + ApiKey + " and Authorization: Bearer abc.def-ghi");
        Assert.Contains("sk-[redacted]", text, StringComparison.Ordinal);
        Assert.Contains("Bearer [redacted]", text, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiKey, text, StringComparison.Ordinal);
        Assert.DoesNotContain("abc.def-ghi", text, StringComparison.Ordinal);
    }
}
