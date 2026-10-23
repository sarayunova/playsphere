using System.Text.RegularExpressions;

namespace PlaySphere.Application.Diagnostics;

/// <summary>
/// Strips OpenAI-style keys and bearer tokens from text that is about to leave the machine
/// inside a tester-sent report. Library database bytes are not passed through here.
/// </summary>
public static partial class DiagnosticSecretRedactor
{
    [GeneratedRegex(@"sk-[A-Za-z0-9_\-]{16,}", RegexOptions.CultureInvariant)]
    private static partial Regex OpenAiKeyPattern();

    [GeneratedRegex(@"Bearer\s+\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BearerPattern();

    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var redacted = OpenAiKeyPattern().Replace(text, "sk-[redacted]");
        return BearerPattern().Replace(redacted, "Bearer [redacted]");
    }
}
