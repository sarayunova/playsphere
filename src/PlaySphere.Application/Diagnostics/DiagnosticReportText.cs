using System.Globalization;
using System.Text;

namespace PlaySphere.Application.Diagnostics;

/// <summary>The generated <c>report.txt</c> stored inside the zip. User text is capped and redacted.</summary>
public sealed class DiagnosticReportDocument
{
    public DateTimeOffset CreatedAtUtc { get; init; }

    public string? AppVersion { get; init; }

    public string? OsDescription { get; init; }

    public string? Description { get; init; }

    public string? Contact { get; init; }

    public bool LibraryDatabaseRequested { get; init; }

    public IReadOnlyList<string> IncludedFiles { get; init; } = [];

    public IReadOnlyList<string> MissingLogs { get; init; } = [];

    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>
/// User-facing text for the report file, the mailto body, and the confirmation dialog.
/// Mail clients cannot attach a file to a <c>mailto:</c> URI (RFC 6068), so the body tells
/// the tester to attach the zip Explorer has selected.
/// </summary>
public static class DiagnosticReportText
{
    public const int MaxDescriptionChars = 4000;

    public const int MaxContactChars = 200;

    public const string MailSubject = "LiVi PlaySphere Studio diagnostic report";

    public static string SanitizeDescription(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var cleaned = text.Replace("\0", string.Empty, StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        if (cleaned.Length > MaxDescriptionChars)
        {
            cleaned = cleaned[..MaxDescriptionChars];
        }

        return cleaned;
    }

    public static string SanitizeContact(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var cleaned = text.Replace("\0", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal)
            .Trim();
        if (cleaned.Length > MaxContactChars)
        {
            cleaned = cleaned[..MaxContactChars];
        }

        return cleaned;
    }

    public static string BuildReport(DiagnosticReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var description = SanitizeDescription(DiagnosticSecretRedactor.Redact(document.Description));
        var contact = SanitizeContact(DiagnosticSecretRedactor.Redact(document.Contact));
        var created = document.CreatedAtUtc.ToUniversalTime()
            .ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);

        var builder = new StringBuilder();
        builder.AppendLine("LiVi PlaySphere Studio diagnostic report");
        builder.Append("Created (UTC): ").AppendLine(created);
        builder.Append("App version: ").AppendLine(string.IsNullOrWhiteSpace(document.AppVersion) ? "unknown" : document.AppVersion.Trim());
        builder.Append("OS: ").AppendLine(string.IsNullOrWhiteSpace(document.OsDescription) ? "unknown" : document.OsDescription.Trim());
        builder.AppendLine();
        builder.Append("Reply contact: ").AppendLine(contact.Length == 0 ? "(none)" : contact);
        builder.AppendLine();
        builder.AppendLine("What happened:");
        builder.AppendLine(description.Length == 0 ? "(no description)" : description);
        builder.AppendLine();
        builder.AppendLine("Included files:");
        AppendBullets(builder, document.IncludedFiles, "(none)");
        builder.AppendLine();
        builder.AppendLine("Missing logs:");
        AppendBullets(builder, document.MissingLogs, "(none)");
        builder.AppendLine();
        builder.AppendLine("Notes:");
        AppendBullets(builder, document.Notes.Select(DiagnosticSecretRedactor.Redact), "(none)");
        builder.AppendLine();
        builder.AppendLine("Privacy:");
        builder.AppendLine(LibraryLine(document.LibraryDatabaseRequested, document.IncludedFiles));
        builder.AppendLine("Media files, thumbnails, preferences.json, and source media are not included.");
        builder.AppendLine("OpenAI-style API keys and Bearer tokens found in logs or notes are redacted.");
        builder.AppendLine("This file was created on this PC. PlaySphere does not upload it.");
        return builder.ToString();
    }

    public static string FormatFileList(DiagnosticReportInventory inventory, bool includeLibraryDatabase)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        var builder = new StringBuilder();
        foreach (var file in inventory.Files.Where(f => f.Kind == DiagnosticFileKind.Log))
        {
            builder.Append(file.FileName).Append(" — ");
            builder.AppendLine(file.Exists
                ? $"will include ({FormatLength(file.LengthBytes)})"
                : "not found, will skip");
        }

        builder.AppendLine();
        builder.AppendLine(includeLibraryDatabase
            ? "Library database — will include if the file is present"
            : "Library database — not included");
        foreach (var file in inventory.Files.Where(f => f.Kind == DiagnosticFileKind.LibraryDatabase))
        {
            builder.Append("  ").Append(file.FileName).Append(" — ");
            if (!file.Exists)
            {
                builder.AppendLine(includeLibraryDatabase ? "not found, will skip" : "not found");
            }
            else if (includeLibraryDatabase)
            {
                builder.Append("will include (").Append(FormatLength(file.LengthBytes)).AppendLine(")");
            }
            else
            {
                builder.Append("present, left out unless you check the box (").Append(FormatLength(file.LengthBytes)).AppendLine(")");
            }
        }

        builder.AppendLine();
        builder.Append("Media files, thumbnails, preferences, and source media are never included.");
        return builder.ToString();
    }

    public static string BuildConfirmation(
        DiagnosticReportInventory inventory,
        bool includeLibraryDatabase,
        string supportEmail)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        var builder = new StringBuilder();
        builder.AppendLine("Nothing is uploaded. PlaySphere will create a zip on this PC and open your email app. You still have to press Send.");
        builder.AppendLine();
        builder.Append("Addressed to: ").AppendLine(supportEmail.Trim());
        var warning = PlaceholderWarning(supportEmail);
        if (warning is not null)
        {
            builder.AppendLine(warning);
        }

        builder.AppendLine();
        builder.AppendLine(FormatFileList(inventory, includeLibraryDatabase));
        builder.AppendLine();
        builder.Append("Your note is saved in the zip as report.txt and copied into the email.");
        return builder.ToString();
    }

    public static string? PlaceholderWarning(string? email)
    {
        if (!DiagnosticReportDefaults.IsPlaceholderAddress(email))
        {
            return null;
        }

        return $"This build still uses a placeholder support address ({email!.Trim()}). Change DiagnosticReportDefaults.SupportEmail before shipping to testers.";
    }

    public static string FormatLength(long bytes)
    {
        if (bytes < 0)
        {
            bytes = 0;
        }

        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024.0:0.#} KB";
        }

        return $"{bytes / (1024.0 * 1024.0):0.#} MB";
    }

    private static string LibraryLine(bool requested, IReadOnlyList<string> included)
    {
        var databaseFiles = included
            .Where(DiagnosticLogCatalog.IsLibraryDatabaseFileName)
            .ToArray();
        if (!requested)
        {
            return "Library database: not included.";
        }

        if (databaseFiles.Length == 0)
        {
            return "Library database: requested, but it was not included.";
        }

        return "Library database: included (" + string.Join(", ", databaseFiles) + ").";
    }

    private static void AppendBullets(StringBuilder builder, IEnumerable<string>? lines, string emptyLabel)
    {
        var any = false;
        if (lines is not null)
        {
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                any = true;
                builder.Append("- ").AppendLine(line.Trim());
            }
        }

        if (!any)
        {
            builder.Append("- ").AppendLine(emptyLabel);
        }
    }
}
