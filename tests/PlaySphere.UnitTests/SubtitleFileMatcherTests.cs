using PlaySphere.Subtitles;
using Xunit;

namespace PlaySphere.UnitTests;

/// <summary>Tests for automatic external-subtitle filename matching (PICKLE.md §17). Pure logic — no engine.</summary>
public sealed class SubtitleFileMatcherTests : IDisposable
{
    private readonly string _directory;

    public SubtitleFileMatcherTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PlaySphereSubtitleMatcherTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string CreateFile(string name)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    [Fact]
    public void FindMatches_finds_exact_basename_subtitle()
    {
        var media = CreateFile("Movie.mp4");
        CreateFile("Movie.srt");

        var matches = SubtitleFileMatcher.FindMatches(media);

        Assert.Single(matches);
        Assert.Null(matches[0].LanguageTag);
        Assert.Equal(SubtitleFormat.Srt, matches[0].Format);
    }

    [Fact]
    public void FindMatches_extracts_language_tag_from_filename()
    {
        var media = CreateFile("Movie.mp4");
        CreateFile("Movie.en.srt");
        CreateFile("Movie.fr.srt");

        var matches = SubtitleFileMatcher.FindMatches(media);

        Assert.Equal(2, matches.Count);
        Assert.Contains(matches, m => m.LanguageTag == "en");
        Assert.Contains(matches, m => m.LanguageTag == "fr");
    }

    [Fact]
    public void FindMatches_ranks_preferred_language_first()
    {
        var media = CreateFile("Movie.mp4");
        CreateFile("Movie.en.srt");
        CreateFile("Movie.fr.srt");

        var matches = SubtitleFileMatcher.FindMatches(media, preferredLanguages: ["fr", "en"]);

        Assert.Equal("fr", matches[0].LanguageTag);
        Assert.Equal("en", matches[1].LanguageTag);
    }

    [Fact]
    public void FindMatches_matches_region_tag_against_primary_language_preference()
    {
        var media = CreateFile("Movie.mp4");
        CreateFile("Movie.en-US.srt");
        CreateFile("Movie.fr.srt");

        var matches = SubtitleFileMatcher.FindMatches(media, preferredLanguages: ["en"]);

        Assert.Equal("en-US", matches[0].LanguageTag);
    }

    [Fact]
    public void FindMatches_does_not_match_unrelated_basename_prefix()
    {
        var media = CreateFile("Movie.mp4");
        CreateFile("Movie2.srt");

        var matches = SubtitleFileMatcher.FindMatches(media);

        Assert.Empty(matches);
    }

    [Fact]
    public void FindMatches_ignores_non_subtitle_extensions()
    {
        var media = CreateFile("Movie.mp4");
        CreateFile("Movie.nfo");
        CreateFile("Movie.jpg");

        var matches = SubtitleFileMatcher.FindMatches(media);

        Assert.Empty(matches);
    }

    [Fact]
    public void FindMatches_returns_empty_when_directory_does_not_exist()
    {
        var missing = Path.Combine(_directory, "does-not-exist", "Movie.mp4");

        var matches = SubtitleFileMatcher.FindMatches(missing);

        Assert.Empty(matches);
    }

    [Fact]
    public void FindMatches_recognizes_ass_ssa_and_sub_for_engine_rendered_formats()
    {
        var media = CreateFile("Movie.mp4");
        CreateFile("Movie.ass");
        CreateFile("Movie.ssa");
        CreateFile("Movie.sub");

        var matches = SubtitleFileMatcher.FindMatches(media);

        Assert.Equal(3, matches.Count);
        Assert.All(matches, m => Assert.Equal(SubtitleFormat.Unknown, m.Format));
    }
}
