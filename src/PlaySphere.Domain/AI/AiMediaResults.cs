using System;

namespace PlaySphere.Domain.AI;

/// <summary>
/// A chapter generated from media content: a title spanning a start timestamp (PICKLE.md §38
/// "automatic chapters"). Earliest-correct title resolution is a later concern; this is the plain
/// result shape the boundary returns. Content is untrusted (PICKLE.md §43).
/// </summary>
public sealed record AiChapter(
    string Title,
    double StartSeconds);

/// <summary>
/// A short summary of media content (PICKLE.md §38 "media summary"). Untrusted until validated.
/// </summary>
public sealed record AiSummary(
    string Text,
    int EstimatedWordCount)
{
    public static AiSummary FromText(string text) =>
        new(text.Trim(), CountWords(text));

    private static int CountWords(string text)
    {
        var count = 0;
        foreach (var part in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            count++;
        }
        return count;
    }
}

/// <summary>
/// A key point / action item extracted from media (PICKLE.md §38 "key points", "action items"),
/// optionally tied to the timestamp where it was mentioned.
/// </summary>
public sealed record AiKeyPoint(
    string Text,
    double? StartSeconds);
