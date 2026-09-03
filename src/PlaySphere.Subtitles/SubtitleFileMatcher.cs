namespace PlaySphere.Subtitles;

/// <summary>A subtitle file discovered next to a media file, with its language tag if one was encoded in the filename.</summary>
public sealed record SubtitleFileMatch(string FilePath, string? LanguageTag, SubtitleFormat Format);

/// <summary>
/// Automatic external-subtitle filename matching (PICKLE.md §17). Looks only in the media
/// file's own directory (never recursive, never other drives — PICKLE.md §22) for files named
/// <c>{basename}.{ext}</c> or <c>{basename}.{language}.{ext}</c>.
/// </summary>
public static class SubtitleFileMatcher
{
    private static readonly string[] RecognizedExtensions = [".srt", ".ass", ".ssa", ".vtt", ".sub"];

    /// <summary>
    /// Finds subtitle files matching <paramref name="mediaFilePath"/>'s base name in the same
    /// directory, ordered by <paramref name="preferredLanguages"/> (most preferred first; a
    /// language may be a bare tag like <c>"en"</c> or a region tag like <c>"en-US"</c> — a
    /// candidate's primary subtag is checked too). Files with no language tag rank just after
    /// explicit preference matches. Returns an empty list if the directory does not exist.
    /// </summary>
    public static IReadOnlyList<SubtitleFileMatch> FindMatches(string mediaFilePath, IReadOnlyList<string>? preferredLanguages = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaFilePath);

        var directory = Path.GetDirectoryName(Path.GetFullPath(mediaFilePath));
        var baseName = Path.GetFileNameWithoutExtension(mediaFilePath);
        if (directory is null || baseName.Length == 0 || !Directory.Exists(directory))
        {
            return [];
        }

        var matches = new List<SubtitleFileMatch>();
        foreach (var candidate in Directory.EnumerateFiles(directory))
        {
            if (!RecognizedExtensions.Contains(Path.GetExtension(candidate).ToLowerInvariant()))
            {
                continue;
            }

            var candidateName = Path.GetFileNameWithoutExtension(candidate);
            if (!candidateName.StartsWith(baseName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var remainder = candidateName[baseName.Length..];
            if (remainder.Length > 0 && remainder[0] != '.')
            {
                continue; // e.g. base "Movie" must not match "Movie2.srt"
            }

            var language = remainder.Length > 1 ? remainder[1..] : null;
            matches.Add(new SubtitleFileMatch(candidate, language, SubtitleParserFactory.DetectFormat(candidate)));
        }

        var preferenceRank = BuildPreferenceRank(preferredLanguages);
        return matches
            .OrderBy(m => RankOf(m.LanguageTag, preferenceRank))
            .ThenBy(m => m.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, int> BuildPreferenceRank(IReadOnlyList<string>? preferredLanguages)
    {
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (preferredLanguages is null)
        {
            return rank;
        }

        for (var i = 0; i < preferredLanguages.Count; i++)
        {
            rank.TryAdd(preferredLanguages[i], i);
        }

        return rank;
    }

    private static int RankOf(string? languageTag, Dictionary<string, int> preferenceRank)
    {
        if (languageTag is null)
        {
            return preferenceRank.Count;
        }

        if (preferenceRank.TryGetValue(languageTag, out var exact))
        {
            return exact;
        }

        var primary = languageTag.Split('-')[0];
        if (preferenceRank.TryGetValue(primary, out var byPrimary))
        {
            return byPrimary;
        }

        return preferenceRank.Count + 1;
    }
}
