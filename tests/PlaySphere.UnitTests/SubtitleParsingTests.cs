using PlaySphere.Subtitles;
using Xunit;

namespace PlaySphere.UnitTests;

/// <summary>Tests for defensive SRT/WebVTT parsing (PICKLE.md §17, §43). Pure logic — no engine.</summary>
public sealed class SubtitleParsingTests
{
    private const string ValidSrt =
        "1\n00:00:20,000 --> 00:00:24,400\nFirst line\nSecond line\n\n" +
        "2\n00:00:24,600 --> 00:00:27,800\nAnother cue\n";

    [Fact]
    public void Srt_parses_multiline_cues_in_order()
    {
        var doc = new SrtSubtitleParser().Parse(ValidSrt);

        Assert.Equal(SubtitleFormat.Srt, doc.Format);
        Assert.Equal(2, doc.Cues.Count);
        Assert.Equal(TimeSpan.Parse("00:00:20.000"), doc.Cues[0].Start);
        Assert.Equal(TimeSpan.Parse("00:00:24.400"), doc.Cues[0].End);
        Assert.Equal("First line\nSecond line", doc.Cues[0].Text);
        Assert.Equal("Another cue", doc.Cues[1].Text);
    }

    [Fact]
    public void Srt_handles_crlf_line_endings()
    {
        var crlf = ValidSrt.Replace("\n", "\r\n");
        var doc = new SrtSubtitleParser().Parse(crlf);

        Assert.Equal(2, doc.Cues.Count);
        Assert.Equal("First line\nSecond line", doc.Cues[0].Text);
    }

    [Fact]
    public void Srt_skips_malformed_block_but_keeps_valid_ones()
    {
        var content =
            "1\nnot a timestamp\nOrphan text\n\n" +
            "2\n00:00:24,600 --> 00:00:27,800\nGood cue\n";

        var doc = new SrtSubtitleParser().Parse(content);

        Assert.Single(doc.Cues);
        Assert.Equal("Good cue", doc.Cues[0].Text);
    }

    [Fact]
    public void Srt_skips_cue_where_end_is_not_after_start()
    {
        var content = "1\n00:00:24,000 --> 00:00:20,000\nBackwards\n";

        var doc = new SrtSubtitleParser().Parse(content);

        Assert.Empty(doc.Cues);
    }

    [Fact]
    public void Srt_renumbers_sequentially_when_a_block_is_skipped()
    {
        var content =
            "1\nnot a timestamp\nOrphan\n\n" +
            "2\n00:00:01,000 --> 00:00:02,000\nFirst kept\n\n" +
            "3\n00:00:03,000 --> 00:00:04,000\nSecond kept\n";

        var doc = new SrtSubtitleParser().Parse(content);

        Assert.Equal([1, 2], doc.Cues.Select(c => c.Index));
    }

    [Fact]
    public void Srt_parse_of_empty_content_returns_no_cues()
    {
        var doc = new SrtSubtitleParser().Parse(string.Empty);
        Assert.Empty(doc.Cues);
    }

    [Fact]
    public void Srt_throws_ArgumentException_when_content_exceeds_limit()
    {
        var oversized = new string('a', SubtitleParserLimits.MaxContentLengthChars + 1);
        Assert.Throws<ArgumentException>(() => new SrtSubtitleParser().Parse(oversized));
    }

    [Fact]
    public void WebVtt_skips_header_note_and_style_blocks()
    {
        var content =
            "WEBVTT - some metadata\n\n" +
            "NOTE this is a comment\nspanning lines\n\n" +
            "STYLE\n::cue { color: red; }\n\n" +
            "00:00:01.000 --> 00:00:04.000\nHello world\n";

        var doc = new WebVttSubtitleParser().Parse(content);

        Assert.Equal(SubtitleFormat.WebVtt, doc.Format);
        Assert.Single(doc.Cues);
        Assert.Equal("Hello world", doc.Cues[0].Text);
    }

    [Fact]
    public void WebVtt_parses_cue_identifier_and_dot_millis()
    {
        var content = "WEBVTT\n\ncue-1\n00:00:01.500 --> 00:00:03.250\nStyled text\n";

        var doc = new WebVttSubtitleParser().Parse(content);

        Assert.Single(doc.Cues);
        Assert.Equal(TimeSpan.Parse("00:00:01.500"), doc.Cues[0].Start);
        Assert.Equal(TimeSpan.Parse("00:00:03.250"), doc.Cues[0].End);
    }

    [Fact]
    public void WebVtt_accepts_minutes_seconds_form_without_hours()
    {
        var content = "WEBVTT\n\n00:01.000 --> 00:04.000\nNo hours component\n";

        var doc = new WebVttSubtitleParser().Parse(content);

        Assert.Single(doc.Cues);
        Assert.Equal(TimeSpan.FromSeconds(1), doc.Cues[0].Start);
    }

    [Theory]
    [InlineData("movie.srt", SubtitleFormat.Srt)]
    [InlineData("movie.SRT", SubtitleFormat.Srt)]
    [InlineData("movie.vtt", SubtitleFormat.WebVtt)]
    [InlineData("movie.ass", SubtitleFormat.Unknown)]
    [InlineData("movie.mp4", SubtitleFormat.Unknown)]
    public void ParserFactory_detects_format_from_extension(string path, SubtitleFormat expected)
    {
        Assert.Equal(expected, SubtitleParserFactory.DetectFormat(path));
    }

    [Fact]
    public void ParserFactory_returns_null_for_unparsed_formats()
    {
        Assert.Null(SubtitleParserFactory.ForFormat(SubtitleFormat.Unknown));
    }

    [Fact]
    public void ParserFactory_ForFile_returns_matching_parser()
    {
        var parser = SubtitleParserFactory.ForFile("movie.vtt");
        Assert.NotNull(parser);
        Assert.Equal(SubtitleFormat.WebVtt, parser!.Format);
    }

    [Fact]
    public void CuesAt_returns_only_cues_active_at_position()
    {
        var doc = new SrtSubtitleParser().Parse(ValidSrt);

        Assert.Empty(doc.CuesAt(TimeSpan.FromSeconds(10)));
        Assert.Single(doc.CuesAt(TimeSpan.FromSeconds(21)));
        Assert.Single(doc.CuesAt(TimeSpan.FromSeconds(25)));
    }

    [Fact]
    public void CuesAt_applies_delay_offset()
    {
        var doc = new SrtSubtitleParser().Parse(ValidSrt);

        // Cue 1 spans 20.0-24.4s; with a +5s delay it becomes active at 27s, not 21s.
        Assert.Empty(doc.CuesAt(TimeSpan.FromSeconds(21), delay: TimeSpan.FromSeconds(5)));
        Assert.Single(doc.CuesAt(TimeSpan.FromSeconds(27), delay: TimeSpan.FromSeconds(5)));
    }
}
