namespace PlaySphere.Domain.Media;

/// <summary>
/// Result of a clip export operation (PICKLE.md §1102 "clip export"). A media file is untrusted
/// input, so the exporter must never throw to the caller — every failure (missing tool, missing
/// input, bound violation, subprocess error, timeout) is reported as a <see cref="Fail"/> result.
/// </summary>
public sealed record ClipExportResult(bool Success, string? ErrorMessage = null)
{
    public static ClipExportResult Ok { get; } = new(true);

    public static ClipExportResult Fail(string message) => new(false, message);
}