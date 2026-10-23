namespace PlaySphere.Application.Diagnostics;

public enum DiagnosticFileKind
{
    Log,
    LibraryDatabase,
}

/// <summary>One allow-listed diagnostic file. The file name is the on-disk name writers use.</summary>
public sealed record DiagnosticLogFile(string FileName, string Description, DiagnosticFileKind Kind);

/// <summary>
/// The only files a diagnostic pack may read. Anything else in the data folder — media,
/// thumbnails, <c>preferences.json</c>, a planted <c>report.txt</c> — is ignored.
/// Names match the writers in <c>App</c>, <c>PlayerPage</c>, and <c>VisualizerAudioTrace</c>.
/// </summary>
public static class DiagnosticLogCatalog
{
    public const int DefaultMaxLogBytes = 2 * 1024 * 1024;

    public const int DefaultMaxLibraryDatabaseBytes = 16 * 1024 * 1024;

    public const string ReportEntryName = "report.txt";

    public const string CrashLogFileName = "crash.log";

    public const string CrashLogRotationFileName = "crash.log.old";

    public const string LibVlcLogFileName = "libvlc.log";

    public const string VisualizerAudioLogFileName = "visualizer-audio.log";

    public static IReadOnlyList<DiagnosticLogFile> LogFiles { get; } =
    [
        new(CrashLogFileName, "Unhandled exceptions and playback errors.", DiagnosticFileKind.Log),
        new(CrashLogRotationFileName, "Previous crash.log, kept after the log grows past 5 MB.", DiagnosticFileKind.Log),
        new(LibVlcLogFileName, "libVLC native playback diagnostics.", DiagnosticFileKind.Log),
        new(VisualizerAudioLogFileName, "Visualizer audio-capture trace.", DiagnosticFileKind.Log),
    ];

    public static IReadOnlyList<DiagnosticLogFile> LibraryDatabaseFiles { get; } =
    [
        new("library.db", "Media library database (file paths and playback history).", DiagnosticFileKind.LibraryDatabase),
        new("library.db-wal", "SQLite write-ahead log for the library database.", DiagnosticFileKind.LibraryDatabase),
        new("library.db-shm", "SQLite shared-memory file for the library database.", DiagnosticFileKind.LibraryDatabase),
    ];

    public static bool IsSafeFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        if (fileName is "." or "..")
        {
            return false;
        }

        // Reject both separators so a Linux host (where '\' is not a directory separator)
        // still cannot be talked into opening a path outside the data folder.
        if (fileName.IndexOfAny(['/', '\\', ':']) >= 0)
        {
            return false;
        }

        return string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal);
    }

    public static bool IsLogFileName(string? fileName) =>
        IsListed(LogFiles, fileName);

    public static bool IsLibraryDatabaseFileName(string? fileName) =>
        IsListed(LibraryDatabaseFiles, fileName);

    /// <summary>
    /// Canonical allow-list filter. <paramref name="topLevelFileNames"/> is the set of names
    /// sitting directly in the data folder (not a recursive walk). Returned names are the
    /// catalog's canonical casing, in catalog order. Library files are included only when
    /// <paramref name="includeLibraryDatabase"/> is true.
    /// </summary>
    public static IReadOnlyList<string> SelectPackFileNames(
        IEnumerable<string>? topLevelFileNames,
        bool includeLibraryDatabase)
    {
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (topLevelFileNames is not null)
        {
            foreach (var name in topLevelFileNames)
            {
                if (IsSafeFileName(name))
                {
                    present.Add(name);
                }
            }
        }

        var selected = new List<string>();
        foreach (var log in LogFiles)
        {
            if (present.Contains(log.FileName))
            {
                selected.Add(log.FileName);
            }
        }

        if (includeLibraryDatabase)
        {
            foreach (var databaseFile in LibraryDatabaseFiles)
            {
                if (present.Contains(databaseFile.FileName))
                {
                    selected.Add(databaseFile.FileName);
                }
            }
        }

        return selected;
    }

    private static bool IsListed(IReadOnlyList<DiagnosticLogFile> files, string? fileName)
    {
        if (!IsSafeFileName(fileName))
        {
            return false;
        }

        foreach (var file in files)
        {
            if (string.Equals(file.FileName, fileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
