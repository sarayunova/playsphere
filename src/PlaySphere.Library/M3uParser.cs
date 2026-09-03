using System.Text;
using PlaySphere.Domain.Library;

namespace PlaySphere.Library;

/// <summary>
/// Bounded, defensive parser for M3U/M3U8 playlist files (PICKLE.md §25, §43 untrusted-input).
/// Handles the <c>#EXTM3U</c> header and <c>#EXTINF:&lt;seconds&gt;,&lt;title&gt;</c> directives,
/// treats any other <c>#</c> line as a comment/extension, resolves relative paths against the
/// playlist file's directory, and caps the number of entries so an attacker-supplied file cannot
/// exhaust resources. Malformed or empty lines are skipped, never thrown.
/// </summary>
public static class M3uParser
{
    /// <summary>Maximum entries honored from one playlist (guards against unbounded files).</summary>
    public const int MaxEntries = 10_000;

    /// <summary>Maximum characters read per line.</summary>
    private const int MaxLineLength = 4096;

    /// <summary>
    /// Parses raw M3U content. <paramref name="playlistFilePath"/> is only used to resolve relative
    /// entries (its directory is the base); pass the path the content was read from.
    /// </summary>
    public static IReadOnlyList<M3uEntry> Parse(string content, string? playlistFilePath = null)
    {
        if (string.IsNullOrEmpty(content))
        {
            return [];
        }

        string? baseDirectory = null;
        if (!string.IsNullOrWhiteSpace(playlistFilePath))
        {
            baseDirectory = Path.GetDirectoryName(playlistFilePath);
        }

        var entries = new List<M3uEntry>();
        string? pendingTitle = null;
        long? pendingDuration = null;

        using var reader = new StringReader(content);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (entries.Count >= MaxEntries)
            {
                break;
            }

            if (line.Length > MaxLineLength)
            {
                line = line[..MaxLineLength];
            }

            var trimmed = line.Trim();

            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed[0] == '#')
            {
                // #EXTINF:<seconds>,<title> applies to the NEXT entry.
                if (trimmed.StartsWith("#EXTINF", StringComparison.OrdinalIgnoreCase))
                {
                    var meta = trimmed["#EXTINF".Length..].TrimStart(':', ' ');
                    var comma = meta.IndexOf(',');
                    var secondsText = comma >= 0 ? meta[..comma] : meta;
                    var titleText = comma >= 0 ? meta[(comma + 1)..].Trim() : null;

                    if (double.TryParse(secondsText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var secs) && secs > 0)
                    {
                        pendingDuration = (long)secs;
                    }
                    pendingTitle = string.IsNullOrWhiteSpace(titleText) ? null : titleText;
                }
                continue;
            }

            // A non-comment, non-directive line is a media path.
            var resolved = ResolvePath(trimmed, baseDirectory);
            if (resolved is not null)
            {
                entries.Add(new M3uEntry(resolved, pendingTitle, pendingDuration));
            }

            // EXTINF metadata applies to exactly one entry.
            pendingTitle = null;
            pendingDuration = null;
        }

        return entries;
    }

    private static string? ResolvePath(string raw, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Strip surrounding quotes some M3U files use, and URI file: scheme if present.
        var path = raw.Trim();
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"')
        {
            path = path[1..^1].Trim();
        }

        foreach (var line in path.Split('\n'))
        {
            var candidate = line.Trim();
            if (candidate.Length == 0) continue;

            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) && uri.IsFile)
            {
                try { candidate = uri.LocalPath; } catch { /* fall through */ }
            }

            try
            {
                if (Path.IsPathRooted(candidate))
                {
                    return Path.GetFullPath(candidate);
                }

                if (!string.IsNullOrWhiteSpace(baseDirectory))
                {
                    return Path.GetFullPath(Path.Combine(baseDirectory, candidate));
                }

                return Path.GetFullPath(candidate);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                return null;
            }
        }

        return null;
    }
}
