using PlaySphere.Domain.Media;
using Xunit;

namespace PlaySphere.UnitTests;

public sealed class AudioTrackSelectorTests
{
    private static AudioTrackInfo Track(int id, string? lang) => new(id, lang, null, "mp4a", 48000, 2);

    [Fact]
    public void PickPreferred_returns_null_without_preferences_or_tracks()
    {
        Assert.Null(AudioTrackSelector.PickPreferred([], null));
        Assert.Null(AudioTrackSelector.PickPreferred([], ["en"]));
        Assert.Null(AudioTrackSelector.PickPreferred([Track(1, "en")], []));
    }

    [Fact]
    public void PickPreferred_selects_highest_priority_language()
    {
        var tracks = new[] { Track(1, "fr"), Track(2, "en"), Track(3, "de") };
        Assert.Equal(2, AudioTrackSelector.PickPreferred(tracks, ["en", "fr"]));
        Assert.Equal(1, AudioTrackSelector.PickPreferred(tracks, ["fr"]));
    }

    [Fact]
    public void PickPreferred_matches_region_tag_via_primary_subtag()
    {
        var tracks = new[] { Track(1, "fr"), Track(2, "en-US") };
        Assert.Equal(2, AudioTrackSelector.PickPreferred(tracks, ["en"]));
        Assert.Equal(2, AudioTrackSelector.PickPreferred(tracks, ["en-US"]));
    }

    [Fact]
    public void PickPreferred_prefers_exact_bare_match_over_region()
    {
        var tracks = new[] { Track(1, "fr"), Track(2, "en"), Track(3, "en-US") };
        // Both "en" and "en-US" match the "en" preference; first in list order (tie) wins.
        Assert.Equal(2, AudioTrackSelector.PickPreferred(tracks, ["en"]));
    }

    [Fact]
    public void PickPreferred_is_case_insensitive()
    {
        var tracks = new[] { Track(1, "EN"), Track(2, "fr") };
        Assert.Equal(1, AudioTrackSelector.PickPreferred(tracks, ["en"]));
    }

    [Fact]
    public void PickPreferred_returns_first_track_on_equal_rank_tie()
    {
        var tracks = new[] { Track(1, "fr"), Track(2, "en"), Track(3, "en") };
        Assert.Equal(2, AudioTrackSelector.PickPreferred(tracks, ["en"]));
    }

    [Fact]
    public void PickPreferred_returns_null_when_no_track_matches()
    {
        var tracks = new[] { Track(1, "fr"), Track(2, "de") };
        Assert.Null(AudioTrackSelector.PickPreferred(tracks, ["ja"]));
    }

    [Fact]
    public void PickPreferred_skips_tracks_without_language()
    {
        var tracks = new[] { Track(1, null), Track(2, "en") };
        Assert.Equal(2, AudioTrackSelector.PickPreferred(tracks, ["en"]));
    }
}
