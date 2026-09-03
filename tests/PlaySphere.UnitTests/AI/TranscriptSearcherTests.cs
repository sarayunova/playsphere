using System.Collections.Generic;
using PlaySphere.Application.AI;
using PlaySphere.Domain.AI;
using PlaySphere.Domain.Library;
using Xunit;

namespace PlaySphere.UnitTests.AI;

public class TranscriptSearcherTests
{
    private static Transcript MakeTranscript(params TranscriptSegment[] segments) => new(segments, "en");

    private static readonly TranscriptSample _sample = new();

    private sealed class TranscriptSample
    {
        public Transcript Transcript { get; }

        public TranscriptSample()
        {
            Transcript = new Transcript(
                new[]
                {
                    new TranscriptSegment(0.0, 3.5, "The quick brown fox jumps over the lazy dog."),
                    new TranscriptSegment(3.5, 7.0, "Machine learning is transforming media understanding."),
                    new TranscriptSegment(7.0, 11.2, "The QUICK brown fox is back again today."),
                },
                "en");
        }
    }

    [Fact]
    public void Search_matches_case_insensitively_across_segments_in_order()
    {
        var matches = TranscriptSearcher.Search(_sample.Transcript, "QUICK");

        Assert.Equal(2, matches.Count);
        Assert.Equal(0.0, matches[0].StartSeconds);
        Assert.Equal(7.0, matches[1].StartSeconds);
    }

    [Fact]
    public void Search_with_no_matches_returns_empty()
    {
        var matches = TranscriptSearcher.Search(_sample.Transcript, "nonexistent");
        Assert.Empty(matches);
    }

    [Fact]
    public void Search_with_whitespace_query_returns_empty()
    {
        Assert.Empty(TranscriptSearcher.Search(_sample.Transcript, "   "));
        Assert.Empty(TranscriptSearcher.Search(_sample.Transcript, null));
    }

    [Fact]
    public void Search_trims_the_query()
    {
        var matches = TranscriptSearcher.Search(_sample.Transcript, "  machine  ");
        Assert.Single(matches);
        Assert.Equal("Machine learning is transforming media understanding.", matches[0].Text);
    }

    [Fact]
    public void Search_on_persisted_segments_matches_and_preserves_order()
    {
        var segments = new List<AiTranscriptSegment>
        {
            new() { Ordinal = 0, StartSeconds = 0, EndSeconds = 5, Text = "Alpha beta gamma." },
            new() { Ordinal = 1, StartSeconds = 5, EndSeconds = 9, Text = "Beta waves in the brain." },
            new() { Ordinal = 2, StartSeconds = 9, EndSeconds = 12, Text = "Nothing here." },
        };

        var matches = TranscriptSearcher.Search(segments, "BETA");

        Assert.Equal(2, matches.Count);
        Assert.Equal(0, matches[0].Ordinal);
        Assert.Equal(1, matches[1].Ordinal);
    }

    [Fact]
    public void Search_on_persisted_segments_empty_query_returns_empty()
    {
        var segments = new List<AiTranscriptSegment> { new() { Ordinal = 0, Text = "hello" } };
        Assert.Empty(TranscriptSearcher.Search(segments, " "));
        Assert.Empty(TranscriptSearcher.Search(segments, null));
    }
}
