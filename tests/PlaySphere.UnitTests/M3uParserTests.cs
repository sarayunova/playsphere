using PlaySphere.Library;
using Xunit;

namespace PlaySphere.UnitTests;

public class M3uParserTests
{
    [Fact]
    public void Parse_empty_and_null_returns_empty()
    {
        Assert.Empty(M3uParser.Parse(""));
        Assert.Empty(M3uParser.Parse("   \n\n  "));
        Assert.Empty(M3uParser.Parse(null!));
    }

    [Fact]
    public void Parse_basic_entries_keeps_order_and_absolute_paths()
    {
        var content = "#EXTM3U\nC:\\Media\\a.mp4\nC:\\Media\\b.mkv\n";

        var entries = M3uParser.Parse(content, @"C:\Media\playlist.m3u");

        Assert.Equal(2, entries.Count);
        Assert.Equal(@"C:\Media\a.mp4", entries[0].Path);
        Assert.Equal(@"C:\Media\b.mkv", entries[1].Path);
    }

    [Fact]
    public void Parse_resolves_relative_paths_against_playlist_directory()
    {
        var content = "a.mp4\nsub/b.mp4\n";

        var entries = M3uParser.Parse(content, @"C:\Media\playlists\list.m3u");

        Assert.Equal(2, entries.Count);
        Assert.Equal(@"C:\Media\playlists\a.mp4", entries[0].Path);
        Assert.Equal(@"C:\Media\playlists\sub\b.mp4", entries[1].Path);
    }

    [Fact]
    public void Parse_extinf_sets_title_and_duration_on_next_entry()
    {
        var content = "#EXTINF:123.5,Movie Title Here\nC:\\Media\\movie.mp4\n";

        var entry = Assert.Single(M3uParser.Parse(content, @"C:\Media\playlist.m3u"));

        Assert.Equal(@"C:\Media\movie.mp4", entry.Path);
        Assert.Equal("Movie Title Here", entry.Title);
        Assert.Equal(123, entry.DurationSeconds);
    }

    [Fact]
    public void Parse_ignores_comments_and_directives_without_entries()
    {
        var content = "#EXTM3U\n#EXT-X-TARGETDURATION:10\n# this is a comment\n\n";
        Assert.Empty(M3uParser.Parse(content, @"C:\Media\playlist.m3u"));
    }

    [Fact]
    public void Parse_extinf_metadata_does_not_leak_across_surrounding_lines()
    {
        var content = "#EXTINF:1,First\nC:\\A.mp4\n#EXTINF:2,\nC:\\B.mp4\nC:\\C.mp4\n";

        var entries = M3uParser.Parse(content, @"C:\Media\list.m3u");

        Assert.Equal(3, entries.Count);
        Assert.Equal("First", entries[0].Title);
        Assert.Null(entries[1].Title); // blank title -> null, duration 2 present
        Assert.Equal(2, entries[1].DurationSeconds);
        Assert.Null(entries[2].Title); // no EXTINF before this -> plain entry
        Assert.Null(entries[2].DurationSeconds);
    }

    [Fact]
    public void Parse_strips_surrounding_quotes()
    {
        var content = "\"C:\\Media\\a.mp4\"\n";
        var entry = Assert.Single(M3uParser.Parse(content, @"C:\Media\list.m3u"));
        Assert.Equal(@"C:\Media\a.mp4", entry.Path);
    }

    [Fact]
    public void Parse_resolves_file_uri()
    {
        var content = "file:///C:/Media/a.mp4\n";
        var entry = Assert.Single(M3uParser.Parse(content, @"C:\Media\list.m3u"));
        Assert.EndsWith(@"C:\Media\a.mp4", entry.Path.Replace('/', '\\'));
    }

    [Fact]
    public void Parse_caps_entry_count()
    {
        var content = string.Join("\n", Enumerable.Range(0, M3uParser.MaxEntries + 50).Select(i => $"C:\\Media\\f{i}.mp4"));
        var entries = M3uParser.Parse(content, @"C:\Media\list.m3u");
        Assert.True(entries.Count <= M3uParser.MaxEntries);
        Assert.Equal(M3uParser.MaxEntries, entries.Count);
    }

    [Fact]
    public void Parse_invalid_paths_are_skipped()
    {
        // Path.GetFullPath throws for this malformed rooted path -> null, skipped.
        var entries = M3uParser.Parse("a.mp4", @"C:\Media\list.m3u");
        Assert.Single(entries);
        Assert.Equal(@"C:\Media\a.mp4", entries[0].Path);
    }
}
