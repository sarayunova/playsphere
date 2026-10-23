using System.Globalization;

namespace PlaySphere.Application.Diagnostics;

/// <summary>
/// On-disk location of PlaySphere's per-user data. Unpackaged app (ADR-0001): classic
/// <see cref="Environment.SpecialFolder.LocalApplicationData"/>, not
/// <c>Windows.Storage.ApplicationData</c>.
/// </summary>
public static class DiagnosticReportLocations
{
    public const string DataFolderName = "LiVi PlaySphere Studio";

    public const string ReportsFolderName = "reports";

    public const string ZipFilePrefix = "PlaySphere-diagnostics-";

    /// <summary>
    /// <c>%LOCALAPPDATA%\LiVi PlaySphere Studio</c>. Writers of logs, preferences, and caches
    /// use this so a diagnostic pack looks in the same folder the app actually writes.
    /// </summary>
    public static string DefaultDataDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            DataFolderName);

    /// <summary>UTC stamp used in zip file names. Invariant so names do not depend on locale.</summary>
    public static string ZipStamp(DateTimeOffset createdAtUtc) =>
        createdAtUtc.ToUniversalTime().ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
}
