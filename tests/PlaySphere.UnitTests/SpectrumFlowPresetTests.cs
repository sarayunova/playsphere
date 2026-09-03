using PlaySphere.UI.Visualizer;
using Xunit;

namespace PlaySphere.UnitTests;

/// <summary>
/// Golden tests for <see cref="SpectrumFlowPreset"/>. The actual Win2D <c>Draw</c> cannot run
/// headlessly (needs a GPU <c>CanvasDevice</c>), so these cover the pure bar→band mapping that
/// determines which spectrum sample each bar reflects (PICKLE.md §37). Rendering itself is a
/// manual-QA pass.
/// </summary>
public class SpectrumFlowPresetTests
{
    [Theory]
    [InlineData(48, 64, 0, 0)]   // first bar → lowest-frequency band
    [InlineData(48, 64, 47, 62)] // last bar → highest-frequency band (min clamps to count-1)
    [InlineData(48, 64, 24, 32)] // mid bar → ~halfway across the spectrum
    [InlineData(48, 32, 24, 16)] // fewer source bands than bars still spans 0..count-1
    [InlineData(10, 64, 9, 57)]  // wide spectrum, few bars
    public void SourceIndexForBar_covers_spectrum_proportionally(int bars, int spectrum, int bar, int expected)
    {
        Assert.Equal(expected, SpectrumFlowPreset.SourceIndexForBar(bars, spectrum, bar));
    }

    [Theory]
    [InlineData(0, 64, 3)]       // no bars → 0
    [InlineData(48, 0, 3)]       // no spectrum → 0
    [InlineData(48, 64, -1)]     // negative bar → 0
    public void SourceIndexForBar_guards_bad_input(int bars, int spectrum, int bar)
    {
        Assert.Equal(0, SpectrumFlowPreset.SourceIndexForBar(bars, spectrum, bar));
    }

    [Fact]
    public void Name_is_Spectrum_Flow()
    {
        Assert.Equal("Spectrum Flow", new SpectrumFlowPreset().Name);
    }

    [Fact]
    public void Default_colors_match_design_palette()
    {
        var preset = new SpectrumFlowPreset();
        Assert.Equal(0xFF, preset.TopColor.A);
        Assert.Equal(0xEE, preset.TopColor.R);
        Assert.Equal(0x49, preset.TopColor.G);
        Assert.Equal(0x99, preset.TopColor.B);
        Assert.Equal(0x36, preset.BottomColor.R);
        Assert.Equal(0xA9, preset.BottomColor.G);
        Assert.Equal(0xFF, preset.BottomColor.B);
    }

    [Fact]
    public void LerpColor_returns_endpoints_at_the_extremes()
    {
        var a = Windows.UI.Color.FromArgb(0xFF, 0x36, 0xA9, 0xFF); // cyan
        var b = Windows.UI.Color.FromArgb(0xFF, 0xEE, 0x49, 0x99); // magenta

        Assert.Equal(a, SpectrumFlowPreset.LerpColor(a, b, 0f));
        Assert.Equal(b, SpectrumFlowPreset.LerpColor(a, b, 1f));
    }

    [Fact]
    public void LerpColor_blends_channels_at_midpoint()
    {
        var a = Windows.UI.Color.FromArgb(0xFF, 0x36, 0xA9, 0xFF);
        var b = Windows.UI.Color.FromArgb(0xFF, 0xEE, 0x49, 0x99);

        var mid = SpectrumFlowPreset.LerpColor(a, b, 0.5f);

        Assert.Equal(0xFF, mid.A);
        Assert.Equal((0x36 + 0xEE) / 2, mid.R);
        Assert.Equal((0xA9 + 0x49) / 2, mid.G);
        Assert.Equal((0xFF + 0x99) / 2, mid.B);
    }

    [Theory]
    [InlineData(-0.5f)]
    [InlineData(1.5f)]
    public void LerpColor_clamps_out_of_range_blend(float t)
    {
        var a = Windows.UI.Color.FromArgb(0xFF, 1, 2, 3);
        var b = Windows.UI.Color.FromArgb(0xFF, 100, 110, 120);

        var color = SpectrumFlowPreset.LerpColor(a, b, t);

        Assert.InRange(color.R, (byte)1, (byte)100);
        Assert.InRange(color.G, (byte)2, (byte)110);
        Assert.InRange(color.B, (byte)3, (byte)120);
    }
}
