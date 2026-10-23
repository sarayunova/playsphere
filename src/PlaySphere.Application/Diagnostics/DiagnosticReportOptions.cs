namespace PlaySphere.Application.Diagnostics;

/// <summary>
/// Startup configuration for the diagnostic report. The support address defaults to
/// <see cref="DiagnosticReportDefaults.SupportEmail"/>; override it here from the composition
/// root when a tester build should use a different mailbox. There is no in-app editor —
/// testers should not be able to retarget the report.
/// </summary>
public sealed class DiagnosticReportOptions
{
    public string SupportEmail { get; init; } = DiagnosticReportDefaults.SupportEmail;

    /// <summary>Overrides <see cref="DiagnosticReportLocations.DefaultDataDirectory"/>. Tests set this.</summary>
    public string? DataDirectory { get; init; }

    /// <summary>Each text log is tailed to this many bytes. Zero or negative uses the catalog default.</summary>
    public int MaxLogBytes { get; init; } = DiagnosticLogCatalog.DefaultMaxLogBytes;

    /// <summary>
    /// Library database files larger than this are skipped (a truncated SQLite file is not useful).
    /// Zero or negative uses the catalog default.
    /// </summary>
    public int MaxLibraryDatabaseBytes { get; init; } = DiagnosticLogCatalog.DefaultMaxLibraryDatabaseBytes;

    /// <summary>Zips in the reports folder older than this many days are deleted when a new report is created. Zero disables cleanup.</summary>
    public int ReportRetentionDays { get; init; } = 14;
}
