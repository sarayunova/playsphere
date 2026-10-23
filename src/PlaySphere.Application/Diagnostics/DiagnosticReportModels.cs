namespace PlaySphere.Application.Diagnostics;

public sealed record DiagnosticFileStatus(
    string FileName,
    string Description,
    DiagnosticFileKind Kind,
    bool Exists,
    long LengthBytes);

public sealed record DiagnosticReportInventory(
    string DataDirectory,
    IReadOnlyList<DiagnosticFileStatus> Files);

/// <summary>What the tester typed, plus optional overrides used by tests.</summary>
public sealed class DiagnosticPackRequest
{
    public string? Description { get; init; }

    public string? Contact { get; init; }

    /// <summary>Default false. Media files are never packed, even when this is true.</summary>
    public bool IncludeLibraryDatabase { get; init; }

    public string? DataDirectory { get; init; }

    public DateTimeOffset? CreatedAtUtc { get; init; }

    public string? AppVersion { get; init; }

    public string? OsDescription { get; init; }
}

public sealed record DiagnosticPackResult(
    bool Success,
    string? ErrorMessage,
    string? ZipPath,
    IReadOnlyList<string> IncludedEntries,
    IReadOnlyList<string> Notes)
{
    public static DiagnosticPackResult Fail(string message) =>
        new(false, message, null, [], []);
}

/// <summary>
/// Builds a zip of local diagnostic logs and a <c>report.txt</c> of what the tester typed.
/// Does not open a mail client and does not transmit anything.
/// </summary>
public interface IDiagnosticReportService
{
    DiagnosticReportInventory Inspect(string? dataDirectory = null);

    Task<DiagnosticPackResult> CreatePackAsync(
        DiagnosticPackRequest request,
        CancellationToken cancellationToken = default);
}
