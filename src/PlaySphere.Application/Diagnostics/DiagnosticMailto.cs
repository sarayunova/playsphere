namespace PlaySphere.Application.Diagnostics;

/// <summary>
/// Builds a <c>mailto:</c> URI. RFC 6068 has no attachment parameter, and the non-standard
/// <c>attach=</c> query only works in some Outlook versions, so this never claims to attach
/// the zip. The body names the file and asks the tester to attach it.
/// </summary>
public static class DiagnosticMailto
{
    public const int MaxUriLength = 1800;

    public static string? TryCreateUri(
        string? supportEmail,
        string? zipPath,
        string? description,
        string? contact,
        IReadOnlyList<string>? includedFiles,
        bool libraryDatabaseRequested)
    {
        if (!DiagnosticReportDefaults.IsPlausibleSupportEmail(supportEmail))
        {
            return null;
        }

        var email = supportEmail!.Trim();
        var sanitized = DiagnosticReportText.SanitizeDescription(DiagnosticSecretRedactor.Redact(description));
        var contactSafe = DiagnosticReportText.SanitizeContact(DiagnosticSecretRedactor.Redact(contact));
        var files = includedFiles ?? [];
        var path = string.IsNullOrWhiteSpace(zipPath) ? "(path unavailable)" : zipPath.Trim();

        var allowance = sanitized.Length;
        while (true)
        {
            var body = BuildBody(path, sanitized, contactSafe, files, libraryDatabaseRequested, allowance);
            var uri = Compose(email, body);
            if (uri.Length <= MaxUriLength)
            {
                return uri;
            }

            if (allowance == 0)
            {
                break;
            }

            allowance = Math.Max(0, allowance - 240);
        }

        var shortPath = string.IsNullOrWhiteSpace(zipPath) ? "the zip" : Path.GetFileName(zipPath);
        var shortUri = Compose(email, BuildBody(shortPath, string.Empty, contactSafe, files, libraryDatabaseRequested, 0));
        return shortUri.Length <= MaxUriLength ? shortUri : null;
    }

    public static string BuildBody(
        string zipPath,
        string sanitizedDescription,
        string sanitizedContact,
        IReadOnlyList<string> includedFiles,
        bool libraryDatabaseRequested,
        int maxDescriptionChars)
    {
        var description = sanitizedDescription ?? string.Empty;
        if (maxDescriptionChars < description.Length)
        {
            description = maxDescriptionChars <= 0
                ? "(full text is in report.txt inside the zip)"
                : description[..maxDescriptionChars] + "… (full text is in report.txt inside the zip)";
        }

        if (description.Length == 0)
        {
            description = "(no description)";
        }

        var contact = string.IsNullOrEmpty(sanitizedContact) ? "(none)" : sanitizedContact;
        var files = includedFiles.Count == 0
            ? "(report.txt only)"
            : string.Join(", ", includedFiles);
        var library = libraryDatabaseRequested
            ? "Library database: included only if it was on disk and under the size limit."
            : "Library database: not included.";

        return string.Join(
            "\n",
            "LiVi PlaySphere Studio diagnostic report",
            "",
            "Please attach this zip, then press Send. A mailto link cannot attach files, so nothing has been uploaded yet.",
            "File Explorer should have the zip selected — drag it into this message, or use Attach.",
            "",
            "Zip:",
            zipPath,
            "",
            "Reply contact: " + contact,
            "",
            "What happened:",
            description,
            "",
            "Included files: " + files,
            library,
            "Media files, thumbnails, preferences, and source media are not included.",
            "Full notes are in report.txt inside the zip.");
    }

    private static string Compose(string email, string body) =>
        $"mailto:{email}?subject={Uri.EscapeDataString(DiagnosticReportText.MailSubject)}&body={Uri.EscapeDataString(body)}";
}
