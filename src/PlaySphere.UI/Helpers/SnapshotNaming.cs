namespace PlaySphere.UI.Helpers;

/// <summary>
/// Builds a safe suggested file name for captured video snapshots (Phase 7).
/// Media titles come from untrusted metadata (PICKLE.md §43), so the derived file stem strips all
/// filename-hostile characters before it is handed to a FileSavePicker as a suggestion — the user
/// can still rename freely, but an injected path fragment can never escape the picker dialog.
/// </summary>
public static class SnapshotNaming
{
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    /// <summary>
    /// Returns a suggested PNG stem of the form "«sanitized title»-snap-yyyyMMdd-HHmmss".
    /// A null/empty/fully-invalid title falls back to the literal "PlaySphere" prefix. The timestamp
    /// is injectable for deterministic tests.
    /// </summary>
    public static string SuggestFileName(string? mediaTitle, DateTimeOffset? at = null)
    {
        var stamp = (at ?? DateTimeOffset.Now).ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
        var title = Sanitize(mediaTitle).Replace(' ', '-');
        return string.IsNullOrWhiteSpace(title) ? $"PlaySphere-snap-{stamp}.png" : $"{title}-snap-{stamp}.png";
    }

    /// <summary>
    /// Strips invalid file-name characters and trims whitespace/cruft from a media title. Returns an
    /// empty string when the result would be empty or purely dots/spaces.
    /// </summary>
    public static string Sanitize(string? mediaTitle)
    {
        if (string.IsNullOrWhiteSpace(mediaTitle))
        {
            return string.Empty;
        }

        var cleaned = new string(mediaTitle.Select(c => InvalidFileNameChars.Contains(c) ? ' ' : c).ToArray())
            .Trim()
            .Trim('.')
            .Trim();

        // Collapse internal runs of spaces (titles like "Movie   (2001)" → "Movie (2001)").
        var collapsed = string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return collapsed;
    }
}