using PlaySphere.UI.Helpers;
using Xunit;

namespace PlaySphere.UnitTests;

/// <summary>
/// Phase 7 (PS-701) — suggested file-name derivation for video snapshots. Media titles are
/// untrusted input, so the suggestions must be filename-safe and deterministic (timestamp injectable).
/// </summary>
public sealed class SnapshotNamingTests
{
    private static readonly DateTimeOffset Fixed = new(2026, 9, 2, 10, 5, 30, TimeSpan.Zero);

    [Fact]
    public void SuggestFileName_appends_sanitized_title_and_timestamp()
    {
        Assert.Equal("My-Movie-snap-20260902-100530.png", SnapshotNaming.SuggestFileName("My Movie", Fixed));
    }

    [Theory]
    [InlineData("A/B\\C:D*E?F\"G<H>I|J", "A-B-C-D-E-F-G-H-I-J")] // all invalid path chars stripped
    [InlineData("  Trailing  and  Inner  ", "Trailing-and-Inner")] // whitespace collapsed + trimmed
    [InlineData("dots...", "dots")] // trailing dots trimmed
    [InlineData("..", "PlaySphere")] // fully-invalid title falls back to the default prefix
    [InlineData("", "PlaySphere")]
    [InlineData(null, "PlaySphere")]
    public void SuggestFileName_sanitizes_untrusted_titles(string? title, string expectedPrefix)
    {
        var fileName = SnapshotNaming.SuggestFileName(title, Fixed);
        Assert.Equal($"{expectedPrefix}-snap-20260902-100530.png", fileName);
    }

    [Fact]
    public void Sanitize_removes_invalid_characters_and_collapses_spaces()
    {
        Assert.Equal("A B C", SnapshotNaming.Sanitize("A\tB  C"));
        Assert.Equal(string.Empty, SnapshotNaming.Sanitize("  ... "));
    }
}