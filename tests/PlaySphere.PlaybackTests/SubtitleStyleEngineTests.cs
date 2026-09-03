using PlaySphere.Domain.Media;
using Xunit;

namespace PlaySphere.PlaybackTests;

/// <summary>
/// Engine-level subtitle style tests against the real <c>LibVlcPlaybackEngine</c>. These only
/// exercise <c>SetSubtitleStyleAsync</c> state/clamping — no media is loaded and no video
/// surface or display is required, so they run headlessly like the rest of the playback suite.
/// </summary>
[Collection(LibVlcCollection.Name)]
public sealed class SubtitleStyleEngineTests
{
    private readonly LibVlcFixture _fixture;

    public SubtitleStyleEngineTests(LibVlcFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SetSubtitleStyleAsync_clamps_out_of_range_fields()
    {
        using var engine = _fixture.CreateEngine();

        await engine.SetSubtitleStyleAsync(new SubtitleStyleOptions(
            TextScalePercent: 5000,
            TextColorArgb: 0x1FFFFFF,
            OutlineColorArgb: -10,
            VerticalMarginPx: 9999,
            Outline: SubtitleOutlineStyle.Normal,
            FontFamily: "Segoe UI"));

        Assert.Equal(500, engine.SubtitleStyle.TextScalePercent);
        Assert.Equal(0xFFFFFF, engine.SubtitleStyle.TextColorArgb);
        Assert.Equal(0x000000, engine.SubtitleStyle.OutlineColorArgb);
        Assert.Equal(1000, engine.SubtitleStyle.VerticalMarginPx);
        Assert.Equal(SubtitleOutlineStyle.Normal, engine.SubtitleStyle.Outline);
        Assert.Equal("Segoe UI", engine.SubtitleStyle.FontFamily);
    }

    [Fact]
    public async Task SetSubtitleStyleAsync_preserves_nullable_defaults_when_unset()
    {
        using var engine = _fixture.CreateEngine();

        await engine.SetSubtitleStyleAsync(SubtitleStyleOptions.Default);

        Assert.Equal(SubtitleStyleOptions.Default, engine.SubtitleStyle);
    }
}
