using System.Globalization;
using System.IO.Compression;
using System.Text;
using PlaySphere.Application.Diagnostics;

namespace PlaySphere.Infrastructure.Diagnostics;

/// <summary>
/// Writes a zip of the allow-listed diagnostic files under the app data folder.
/// Missing files are skipped. Symlinks are not followed. Nothing is uploaded.
/// </summary>
public sealed class ZipDiagnosticReportService : IDiagnosticReportService
{
    private readonly DiagnosticReportOptions _options;

    public ZipDiagnosticReportService(DiagnosticReportOptions? options = null)
    {
        _options = options ?? new DiagnosticReportOptions();
    }

    public DiagnosticReportInventory Inspect(string? dataDirectory = null)
    {
        var directory = ResolveDirectory(dataDirectory);
        var files = new List<DiagnosticFileStatus>(
            DiagnosticLogCatalog.LogFiles.Count + DiagnosticLogCatalog.LibraryDatabaseFiles.Count);
        foreach (var log in DiagnosticLogCatalog.LogFiles)
        {
            files.Add(Stat(directory, log));
        }

        foreach (var databaseFile in DiagnosticLogCatalog.LibraryDatabaseFiles)
        {
            files.Add(Stat(directory, databaseFile));
        }

        return new DiagnosticReportInventory(directory, files);
    }

    public Task<DiagnosticPackResult> CreatePackAsync(
        DiagnosticPackRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() => CreatePack(request, cancellationToken), cancellationToken);
    }

    private DiagnosticPackResult CreatePack(DiagnosticPackRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dataDirectory = ResolveDirectory(request.DataDirectory);
        string reportsDirectory;
        try
        {
            reportsDirectory = Path.Combine(dataDirectory, DiagnosticReportLocations.ReportsFolderName);
            Directory.CreateDirectory(reportsDirectory);
            DeleteExpiredReports(reportsDirectory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DiagnosticPackResult.Fail(
                "Couldn't create the reports folder. You can still open the log folder and attach the files yourself. " + ex.Message);
        }

        var created = request.CreatedAtUtc ?? DateTimeOffset.UtcNow;
        var zipPath = NextZipPath(reportsDirectory, created);
        var partialPath = zipPath + ".partial";
        var notes = new List<string>();
        var packed = new List<string>();

        try
        {
            if (File.Exists(partialPath))
            {
                File.Delete(partialPath);
            }

            var presentNames = TopLevelFileNames(dataDirectory);
            var selected = DiagnosticLogCatalog.SelectPackFileNames(presentNames, request.IncludeLibraryDatabase);
            var selectedSet = new HashSet<string>(selected, StringComparer.OrdinalIgnoreCase);

            foreach (var log in DiagnosticLogCatalog.LogFiles)
            {
                if (!selectedSet.Contains(log.FileName))
                {
                    notes.Add($"{log.FileName} not found");
                }
            }

            if (request.IncludeLibraryDatabase)
            {
                foreach (var databaseFile in DiagnosticLogCatalog.LibraryDatabaseFiles)
                {
                    if (!selectedSet.Contains(databaseFile.FileName))
                    {
                        notes.Add($"{databaseFile.FileName} not found");
                    }
                }
            }

            using (var stream = new FileStream(partialPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                foreach (var name in selected)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var source = Path.Combine(dataDirectory, name);
                    if (!IsDirectChild(dataDirectory, name, source))
                    {
                        notes.Add($"{name} was skipped because its path is not inside the data folder.");
                        continue;
                    }

                    if (TryAddEntry(zip, source, name, notes))
                    {
                        packed.Add(name);
                    }
                }

                var missingLogs = DiagnosticLogCatalog.LogFiles
                    .Select(file => file.FileName)
                    .Where(name => !packed.Contains(name, StringComparer.OrdinalIgnoreCase))
                    .ToArray();
                var included = new List<string> { DiagnosticLogCatalog.ReportEntryName };
                included.AddRange(packed);
                var report = DiagnosticReportText.BuildReport(new DiagnosticReportDocument
                {
                    CreatedAtUtc = created,
                    AppVersion = request.AppVersion,
                    OsDescription = request.OsDescription ?? Environment.OSVersion.VersionString,
                    Description = request.Description,
                    Contact = request.Contact,
                    LibraryDatabaseRequested = request.IncludeLibraryDatabase,
                    IncludedFiles = included,
                    MissingLogs = missingLogs,
                    Notes = notes,
                });
                var reportEntry = zip.CreateEntry(DiagnosticLogCatalog.ReportEntryName, CompressionLevel.Optimal);
                using var writer = new StreamWriter(reportEntry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(report);
                packed.Insert(0, DiagnosticLogCatalog.ReportEntryName);
            }

            File.Move(partialPath, zipPath, overwrite: true);
            return new DiagnosticPackResult(true, null, zipPath, packed, notes);
        }
        catch (OperationCanceledException)
        {
            TryDelete(partialPath);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            TryDelete(partialPath);
            return DiagnosticPackResult.Fail(
                "Couldn't create the diagnostic zip. You can still open the log folder and attach the files yourself. " + ex.Message);
        }
    }

    private bool TryAddEntry(ZipArchive zip, string sourcePath, string entryName, List<string> notes)
    {
        try
        {
            var info = new FileInfo(sourcePath);
            if (!info.Exists || info.Attributes.HasFlag(FileAttributes.ReparsePoint) || info.LinkTarget is not null)
            {
                notes.Add($"{entryName} was skipped because it is not a regular file.");
                return false;
            }

            var textLog = DiagnosticLogCatalog.IsLogFileName(entryName);
            var maxBytes = textLog ? MaxLogBytes : MaxLibraryDatabaseBytes;
            if (!textLog && info.Length > maxBytes)
            {
                notes.Add($"{entryName} was skipped because it is {info.Length} bytes (limit {maxBytes}).");
                return false;
            }

            byte[] payload;
            using (var input = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                if (textLog && info.Length > maxBytes && !input.CanSeek)
                {
                    notes.Add($"{entryName} was skipped because it is larger than {maxBytes} bytes and could not be tailed.");
                    return false;
                }

                payload = textLog
                    ? ReadLogBytes(input, info.Length, maxBytes, entryName, notes)
                    : ReadAll(input);
            }

            var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
            using var output = entry.Open();
            output.Write(payload, 0, payload.Length);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            notes.Add($"{entryName} could not be read ({ex.Message}).");
            return false;
        }
    }

    private static byte[] ReadLogBytes(Stream input, long length, int maxBytes, string entryName, List<string> notes)
    {
        var truncated = length > maxBytes;
        if (truncated)
        {
            if (input.CanSeek)
            {
                input.Seek(length - maxBytes, SeekOrigin.Begin);
            }

            notes.Add($"{entryName} truncated to the last {maxBytes} bytes of {length}.");
        }

        using var buffer = new MemoryStream();
        if (truncated)
        {
            var prefix = Encoding.UTF8.GetBytes($"[truncated to the last {maxBytes} bytes]{Environment.NewLine}");
            buffer.Write(prefix, 0, prefix.Length);
        }

        input.CopyTo(buffer);
        var redacted = DiagnosticSecretRedactor.Redact(Encoding.UTF8.GetString(buffer.ToArray()));
        return Encoding.UTF8.GetBytes(redacted);
    }

    private static byte[] ReadAll(Stream input)
    {
        using var buffer = new MemoryStream();
        input.CopyTo(buffer);
        return buffer.ToArray();
    }

    private void DeleteExpiredReports(string reportsDirectory)
    {
        var days = _options.ReportRetentionDays;
        if (days <= 0 || !Directory.Exists(reportsDirectory))
        {
            return;
        }

        var cutoff = DateTime.UtcNow.AddDays(-days);
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(reportsDirectory).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            if (!name.StartsWith(DiagnosticReportLocations.ZipFilePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            var isZip = name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".zip.partial", StringComparison.OrdinalIgnoreCase);
            if (!isZip)
            {
                continue;
            }

            try
            {
                if (File.GetLastWriteTimeUtc(path) < cutoff)
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A locked old zip must not block a new report.
            }
        }
    }

    private DiagnosticFileStatus Stat(string directory, DiagnosticLogFile file)
    {
        long length = 0;
        var exists = false;
        try
        {
            if (DiagnosticLogCatalog.IsSafeFileName(file.FileName))
            {
                var path = Path.Combine(directory, file.FileName);
                var info = new FileInfo(path);
                if (info.Exists && info.LinkTarget is null && !info.Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    exists = true;
                    length = info.Length;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            exists = false;
        }

        return new DiagnosticFileStatus(file.FileName, file.Description, file.Kind, exists, length);
    }

    private static IReadOnlyList<string> TopLevelFileNames(string dataDirectory)
    {
        if (!Directory.Exists(dataDirectory))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateFiles(dataDirectory)
                .Select(Path.GetFileName)
                .OfType<string>()
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool IsDirectChild(string dataDirectory, string fileName, string combinedPath)
    {
        if (!DiagnosticLogCatalog.IsSafeFileName(fileName))
        {
            return false;
        }

        var root = Path.GetFullPath(dataDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(combinedPath);
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(full), fileName, StringComparison.OrdinalIgnoreCase);
    }

    private string NextZipPath(string reportsDirectory, DateTimeOffset created)
    {
        var stamp = DiagnosticReportLocations.ZipStamp(created);
        var path = Path.Combine(reportsDirectory, DiagnosticReportLocations.ZipFilePrefix + stamp + ".zip");
        var suffix = 2;
        while (File.Exists(path) && suffix < 100)
        {
            path = Path.Combine(
                reportsDirectory,
                DiagnosticReportLocations.ZipFilePrefix + stamp + "-" + suffix.ToString(CultureInfo.InvariantCulture) + ".zip");
            suffix++;
        }

        return path;
    }

    private string ResolveDirectory(string? requested)
    {
        var raw = !string.IsNullOrWhiteSpace(requested)
            ? requested
            : !string.IsNullOrWhiteSpace(_options.DataDirectory)
                ? _options.DataDirectory!
                : DiagnosticReportLocations.DefaultDataDirectory();
        try
        {
            return Path.GetFullPath(raw);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return raw;
        }
    }

    private int MaxLogBytes => _options.MaxLogBytes > 0
        ? _options.MaxLogBytes
        : DiagnosticLogCatalog.DefaultMaxLogBytes;

    private int MaxLibraryDatabaseBytes => _options.MaxLibraryDatabaseBytes > 0
        ? _options.MaxLibraryDatabaseBytes
        : DiagnosticLogCatalog.DefaultMaxLibraryDatabaseBytes;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort. A leftover .partial is eligible for retention cleanup later.
        }
    }
}
