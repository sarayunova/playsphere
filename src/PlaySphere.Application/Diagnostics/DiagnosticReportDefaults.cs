namespace PlaySphere.Application.Diagnostics;

/// <summary>
/// Where a tester's diagnostic email is addressed. The shipped default is the LiVi
/// support mailbox. <see cref="DiagnosticReportOptions.SupportEmail"/> can override it
/// at startup, and nothing else should hard-code a destination.
/// </summary>
public static class DiagnosticReportDefaults
{
    public const string SupportEmail = "contact@liviverse.com";

    /// <summary>
    /// True when <paramref name="email"/> is a plausible address whose domain ends in the
    /// reserved <c>.example</c> TLD. The Help page warns until this is replaced with a real mailbox.
    /// </summary>
    public static bool IsPlaceholderAddress(string? email) =>
        IsPlausibleSupportEmail(email)
        && email!.Trim().EndsWith(".example", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Rejects empty values, header-injection characters, and addresses with no domain dot.
    /// This is a mail-header guard, not a full RFC 5322 parser.
    /// </summary>
    public static bool IsPlausibleSupportEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var trimmed = email.Trim();
        if (trimmed.Length is < 5 or > 254)
        {
            return false;
        }

        foreach (var c in trimmed)
        {
            if (char.IsWhiteSpace(c) || c is '<' or '>' or '"' or ',' or ';' or '\\' or '\0')
            {
                return false;
            }
        }

        var at = trimmed.IndexOf('@');
        if (at <= 0 || at != trimmed.LastIndexOf('@'))
        {
            return false;
        }

        var domain = trimmed[(at + 1)..];
        if (domain.Length < 3 || domain.StartsWith('.') || domain.EndsWith('.') || domain.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        return domain.Contains('.', StringComparison.Ordinal);
    }
}
