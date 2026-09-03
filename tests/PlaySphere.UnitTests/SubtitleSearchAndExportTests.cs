using PlaySphere.Subtitles;
using Xunit;

namespace PlaySphere.UnitTests;

/// <summary>Tests for subtitle cue search and SRT export (PICKLE.md §17). Pure logic — no engine.</summary>
public sealed class SubtitleSearchAndExportTests
{
    private const string TwoCueSrt =
        "1\n00:00:20,000 --> 00:00:24,400\nHello world\n\n" +
        "2\n00:00:24,600 --> 00:00:27,800\nGoodbye now\n";

    [Fact]
    public void Search_finds_case_insensitive_substring_match()
    {
        var doc = new SrtSubtitleParser().Parse(TwoCueSrt);

        var results = doc.Search("HELLO").ToList();

        Assert.Single(results);
        Assert.Equal("Hello world", results[0].Text);
    }

    [Fact]
    public void Search_returns_all_matching_cues_in_order()
    {
        var doc = new SrtSubtitleParser().Parse(
            "1\n00:00:01,000 --> 00:00:02,000\nfoo one\n\n" +
            "2\n00:00:03,000 --> 00:00:04,000\nbar\n\n" +
            "3\n00:00:05,000 --> 00:00:06,000\nfoo two\n");

        var results = doc.Search("foo").ToList();

        Assert.Equal(2, results.Count);
        Assert.Equal("foo one", results[0].Text);
        Assert.Equal("foo two", results[1].Text);
    }

    [Fact]
    public void Search_with_empty_query_returns_no_cues()
    {
        var doc = new SrtSubtitleParser().Parse(TwoCueSrt);

        Assert.Empty(doc.Search(string.Empty));
        Assert.Empty(doc.Search("   "));
    }

    [Fact]
    public void ToSrt_round_trips_cue_timing_and_text()
    {
        var doc = new SrtSubtitleParser().Parse(TwoCueSrt);

        var exported = SubtitleExporter.ToSrt(doc);
        var reparsed = new SrtSubtitleParser().Parse(exported);

        Assert.Equal(doc.Cues.Count, reparsed.Cues.Count);
        for (var i = 0; i < doc.Cues.Count; i++)
        {
            Assert.Equal(doc.Cues[i].Start, reparsed.Cues[i].Start);
            Assert.Equal(doc.Cues[i].End, reparsed.Cues[i].End);
            Assert.Equal(doc.Cues[i].Text, reparsed.Cues[i].Text);
        }
    }

    [Fact]
    public void ToSrt_renumbers_sequentially_from_one()
    {
        var doc = new SrtSubtitleParser().Parse(TwoCueSrt);

        var exported = SubtitleExporter.ToSrt(doc);

        Assert.StartsWith("1\n00:00:20,000 --> 00:00:24,400", exported);
        Assert.Contains("\n2\n00:00:24,600 --> 00:00:27,800", exported);
    }

    [Fact]
    public void ToSrt_of_empty_document_returns_empty_string()
    {
        var exported = SubtitleExporter.ToSrt(SubtitleDocument.Empty(SubtitleFormat.Srt));
        Assert.Empty(exported);
    }

    [Fact]
    public async Task WriteSrtAsync_writes_parseable_file()
    {
        var doc = new SrtSubtitleParser().Parse(TwoCueSrt);
        var path = Path.Combine(Path.GetTempPath(), $"PlaySphereExportTest_{Guid.NewGuid():N}.srt");

        try
        {
            await SubtitleExporter.WriteSrtAsync(doc, path);

            var written = await File.ReadAllTextAsync(path);
            var reparsed = new SrtSubtitleParser().Parse(written);
            Assert.Equal(2, reparsed.Cues.Count);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
