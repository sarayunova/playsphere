using System.Globalization;

namespace PlaySphere.Domain.Media;

/// <summary>
/// Pure, testable preferred-language audio-track selection (PICKLE.md Chapter on audio tracks /
/// subtitles). Picks the best matching audio track for a media item given an ordered list of
/// preferred languages, mirroring the ranking used by <c>SubtitleFileMatcher</c>: a language may be
/// a bare tag (<c>"en"</c>) or a region tag (<c>"en-US"</c>), and a track's primary subtag is
/// checked too. Returns <c>null</c> when the preference list is empty or no track matches.
/// </summary>
public static class AudioTrackSelector
{
    /// <summary>
    /// Returns the Id of the audio track most preferred by <paramref name="preferredLanguages"/>,
    /// or null when there are no preferences, no audio tracks, or no track matches.
    /// Ties (equal preference rank) resolve to the first track in list order.
    /// </summary>
    public static int? PickPreferred(IReadOnlyList<AudioTrackInfo> tracks, IReadOnlyList<string>? preferredLanguages)
    {
        if (preferredLanguages is null || preferredLanguages.Count == 0 || tracks.Count == 0)
        {
            return null;
        }

        var preferenceRank = BuildPreferenceRank(preferredLanguages);
        if (preferenceRank.Count == 0)
        {
            return null;
        }

        int? bestId = null;
        var bestRank = int.MaxValue;
        var fallbackRank = preferenceRank.Count;

        foreach (var track in tracks)
        {
            if (track.Language is null)
            {
                continue;
            }

            var rank = RankOf(track.Language, preferenceRank, fallbackRank);
            if (rank >= fallbackRank || rank >= bestRank)
            {
                continue;
            }

            bestId = track.Id;
            bestRank = rank;
        }

        return bestId;
    }

    private static Dictionary<string, int> BuildPreferenceRank(IReadOnlyList<string> preferredLanguages)
    {
        var rank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < preferredLanguages.Count; i++)
        {
            rank.TryAdd(PreferredSubtag(preferredLanguages[i]), i);
        }

        return rank;
    }

    private static int RankOf(string languageTag, Dictionary<string, int> preferenceRank, int fallbackRank)
    {
        if (preferenceRank.TryGetValue(languageTag, out var exact))
        {
            return exact;
        }

        var primary = PreferredSubtag(languageTag);
        if (preferenceRank.TryGetValue(primary, out var byPrimary))
        {
            return byPrimary;
        }

        return fallbackRank;
    }

    private static string PreferredSubtag(string tag)
    {
        var trimmed = tag.Trim().ToLowerInvariant();
        var dash = trimmed.IndexOf('-');
        return dash > 0 ? trimmed[..dash] : trimmed;
    }
}
