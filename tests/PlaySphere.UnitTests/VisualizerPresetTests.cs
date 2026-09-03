using PlaySphere.Domain.Visualizer;
using PlaySphere.UI.Visualizer;
using Xunit;

namespace PlaySphere.UnitTests;

/// <summary>
/// Golden tests for the pure helpers of the Phase 6 presets (VIS-06..09). The actual Win2D
/// <c>Draw</c> cannot run headlessly (needs a GPU <c>CanvasDevice</c>), so these cover the pure
/// mapping/envelope drives that determine the visuals from the analysis frame (PICKLE.md §37).
/// Rendering itself is a manual-QA pass.
/// </summary>
public class VisualizerPresetTests
{
    // ── Neon Wave (VIS-06) ────────────────────────────────────────────────

    [Theory]
    [InlineData(96, 64, 0, 0)]
    [InlineData(96, 64, 95, 63)]
    [InlineData(96, 64, 48, 32)]
    [InlineData(96, 32, 48, 16)]
    [InlineData(10, 64, 9, 57)]
    public void NeonWave_SourceIndexForSample_covers_spectrum_proportionally(int points, int spectrum, int sample, int expected)
    {
        Assert.Equal(expected, NeonWavePreset.SourceIndexForSample(points, spectrum, sample));
    }

    [Theory]
    [InlineData(0, 64, 3)]
    [InlineData(96, 0, 3)]
    [InlineData(96, 64, -1)]
    public void NeonWave_SourceIndexForSample_guards_bad_input(int points, int spectrum, int sample)
    {
        Assert.Equal(0, NeonWavePreset.SourceIndexForSample(points, spectrum, sample));
    }

    [Fact]
    public void NeonWave_Name_is_Neon_Wave()
    {
        Assert.Equal("Neon Wave", new NeonWavePreset().Name);
    }

    // ── Orbit Spectrum (VIS-07) ───────────────────────────────────────────

    [Theory]
    [InlineData(64, 64, 0, 0)]
    [InlineData(64, 64, 63, 63)]
    [InlineData(64, 64, 32, 32)]
    [InlineData(64, 32, 32, 16)]
    public void OrbitSpectrum_SourceIndexForBar_covers_spectrum_proportionally(int bars, int spectrum, int bar, int expected)
    {
        Assert.Equal(expected, OrbitSpectrumPreset.SourceIndexForBar(bars, spectrum, bar));
    }

    [Theory]
    [InlineData(0, 64, 3)]
    [InlineData(64, 0, 3)]
    [InlineData(64, 64, -1)]
    public void OrbitSpectrum_SourceIndexForBar_guards_bad_input(int bars, int spectrum, int bar)
    {
        Assert.Equal(0, OrbitSpectrumPreset.SourceIndexForBar(bars, spectrum, bar));
    }

    [Fact]
    public void OrbitSpectrum_Name_is_Orbit_Spectrum()
    {
        Assert.Equal("Orbit Spectrum", new OrbitSpectrumPreset().Name);
    }

    // ── Pulse Rings (VIS-08) ──────────────────────────────────────────────

    [Fact]
    public void PulseRings_ComputeEnvelope_returns_zero_for_silence()
    {
        var frame = MakeFrame(rms: 0f, transient: 0f);
        Assert.Equal(0f, PulseRingsPreset.ComputeEnvelope(frame));
    }

    [Fact]
    public void PulseRings_ComputeEnvelope_grows_with_energy_and_is_bounded()
    {
        var loud = MakeFrame(rms: 1f, transient: 1f, bass: 1f, mid: 1f, high: 1f);
        var quiet = MakeFrame(rms: 0.1f, transient: 0f);

        var high = PulseRingsPreset.ComputeEnvelope(loud);
        var low = PulseRingsPreset.ComputeEnvelope(quiet);

        Assert.InRange(high, 0f, 1f);
        Assert.True(high > low, "louder frame should produce a larger envelope");
    }

    [Fact]
    public void PulseRings_Name_is_Pulse_Rings()
    {
        Assert.Equal("Pulse Rings", new PulseRingsPreset().Name);
    }

    // ── Particle Bloom (VIS-09) ───────────────────────────────────────────

    [Fact]
    public void ParticleBloom_ComputeDrive_returns_zero_for_silence()
    {
        var frame = MakeFrame(rms: 0f, transient: 0f);
        Assert.Equal(0f, ParticleBloomPreset.ComputeDrive(frame));
    }

    [Fact]
    public void ParticleBloom_ComputeDrive_is_bounded_and_grows_with_energy()
    {
        var loud = MakeFrame(rms: 1f, transient: 1f, bass: 1f, mid: 1f, high: 1f, peak: 1f);
        var quiet = MakeFrame(rms: 0f, transient: 0f);

        var high = ParticleBloomPreset.ComputeDrive(loud);
        var low = ParticleBloomPreset.ComputeDrive(quiet);

        Assert.InRange(high, 0f, 1f);
        Assert.True(high > low, "louder frame should produce a larger drive");
    }

    [Fact]
    public void ParticleBloom_ParticleRadius_scales_with_drive()
    {
        var calm = ParticleBloomPreset.ParticleRadius(0.5f, 0f, 2f, 7f);
        var energetic = ParticleBloomPreset.ParticleRadius(0.5f, 1f, 2f, 7f);

        Assert.True(energetic > calm, "higher drive should bloom larger particles");
        Assert.InRange(calm, 2f, 7f);
        Assert.InRange(energetic, 2f, 7f);
    }

    [Fact]
    public void ParticleBloom_ParticleAlpha_is_bounded_by_max_alpha()
    {
        var alpha = ParticleBloomPreset.ParticleAlpha(0.7f, 1f, 0xAF);
        Assert.InRange(alpha, (byte)0, (byte)0xAF);
    }

    [Fact]
    public void ParticleBloom_Name_is_Particle_Bloom()
    {
        Assert.Equal("Particle Bloom", new ParticleBloomPreset().Name);
    }

    // ── Aurora Ribbon ─────────────────────────────────────────────────────

    [Theory]
    [InlineData(64, 64, 0, 0)]
    [InlineData(64, 64, 63, 63)]
    [InlineData(64, 64, 32, 32)]
    [InlineData(64, 32, 32, 16)]
    public void AuroraRibbon_SourceIndexForSample_covers_spectrum_proportionally(int points, int spectrum, int sample, int expected)
    {
        Assert.Equal(expected, AuroraRibbonPreset.SourceIndexForSample(points, spectrum, sample));
    }

    [Theory]
    [InlineData(0, 64, 3)]
    [InlineData(64, 0, 3)]
    [InlineData(64, 64, -1)]
    public void AuroraRibbon_SourceIndexForSample_guards_bad_input(int points, int spectrum, int sample)
    {
        Assert.Equal(0, AuroraRibbonPreset.SourceIndexForSample(points, spectrum, sample));
    }

    [Theory]
    [InlineData(0f, 100f, 0f)]
    [InlineData(0.5f, 100f, 50f)]
    [InlineData(1f, 100f, 100f)]
    [InlineData(2f, 100f, 100f)]  // energy clamped above 1
    [InlineData(0.3f, 50f, 15f)]
    public void AuroraRibbon_RibbonOffset_clamps_and_scales(float energy, float maxAmp, float expected)
    {
        Assert.Equal(expected, AuroraRibbonPreset.RibbonOffset(energy, maxAmp), 4);
    }

    [Fact]
    public void AuroraRibbon_PaletteColor_starts_at_cool_color()
    {
        var color = AuroraRibbonPreset.PaletteColor(0f, 64f, 0xE0);
        Assert.Equal(0xE0, color.A);
        Assert.Equal(0x36, color.R);
        Assert.Equal(0xA9, color.G);
        Assert.Equal(0xFF, color.B);
    }

    [Fact]
    public void AuroraRibbon_PaletteColor_ends_at_warm_color()
    {
        var color = AuroraRibbonPreset.PaletteColor(64f, 64f, 0xE0);
        Assert.Equal(0xE0, color.A);
        Assert.Equal(0xEC, color.R);
        Assert.Equal(0x48, color.G);
        Assert.Equal(0x99, color.B);
    }

    [Fact]
    public void AuroraRibbon_Name_is_Aurora_Ribbon()
    {
        Assert.Equal("Aurora Ribbon", new AuroraRibbonPreset().Name);
    }

    // ── Prism Tunnel ──────────────────────────────────────────────────────

    [Theory]
    [InlineData(72, 64, 0, 0)]
    [InlineData(72, 64, 71, 63)]
    [InlineData(72, 64, 36, 32)]
    public void PrismTunnel_SourceIndexForSegment_covers_spectrum_proportionally(int segments, int spectrum, int segment, int expected)
    {
        Assert.Equal(expected, PrismTunnelPreset.SourceIndexForSegment(segments, spectrum, segment));
    }

    [Theory]
    [InlineData(0, 64, 3)]
    [InlineData(72, 0, 3)]
    [InlineData(72, 64, -1)]
    public void PrismTunnel_SourceIndexForSegment_guards_bad_input(int segments, int spectrum, int segment)
    {
        Assert.Equal(0, PrismTunnelPreset.SourceIndexForSegment(segments, spectrum, segment));
    }

    [Fact]
    public void PrismTunnel_RingPulse_is_bounded_and_inner_rings_pulse_more()
    {
        var inner = PrismTunnelPreset.RingPulse(1f, 0f, 0.28f);
        var outer = PrismTunnelPreset.RingPulse(1f, 1f, 0.28f);

        Assert.InRange(inner, 0f, 0.28f);
        Assert.InRange(outer, 0f, 0.28f);
        Assert.True(inner > outer, "inner ring should pulse more than the outer ring");
    }

    [Fact]
    public void PrismTunnel_RingPulse_zero_for_silence()
    {
        Assert.Equal(0f, PrismTunnelPreset.RingPulse(0f, 0f, 0.28f));
    }

    [Fact]
    public void PrismTunnel_Name_is_Prism_Tunnel()
    {
        Assert.Equal("Prism Tunnel", new PrismTunnelPreset().Name);
    }

    // ── Album Pulse ───────────────────────────────────────────────────────

    [Fact]
    public void AlbumPulse_ComputeDrive_returns_zero_for_silence()
    {
        var frame = MakeFrame(rms: 0f, transient: 0f);
        Assert.Equal(0f, AlbumPulsePreset.ComputeDrive(frame));
    }

    [Fact]
    public void AlbumPulse_ComputeDrive_is_bounded_and_grows_with_energy()
    {
        var loud = MakeFrame(rms: 1f, transient: 1f, bass: 1f, mid: 1f, high: 1f);
        var quiet = MakeFrame(rms: 0f, transient: 0f);

        var high = AlbumPulsePreset.ComputeDrive(loud);
        var low = AlbumPulsePreset.ComputeDrive(quiet);

        Assert.InRange(high, 0f, 1f);
        Assert.True(high > low, "louder frame should produce a larger drive");
    }

    [Theory]
    [InlineData(0, 60, 0f, 10f, 50f, 0.72f)]
    [InlineData(1, 60, 1f, 10f, 50f, 0.72f)]
    public void AlbumPulse_RayLength_is_bounded_by_geometry(int ray, int rayCount, float drive, float baseRadius, float maxRadius, float pulseDepth)
    {
        var length = AlbumPulsePreset.RayLength(ray, rayCount, drive, baseRadius, maxRadius, pulseDepth);
        Assert.InRange(length, baseRadius, maxRadius);
    }

    [Fact]
    public void AlbumPulse_RayLength_grows_with_drive()
    {
        var calm = AlbumPulsePreset.RayLength(3, 60, 0f, 10f, 50f, 0.72f);
        var energetic = AlbumPulsePreset.RayLength(3, 60, 1f, 10f, 50f, 0.72f);
        Assert.True(energetic > calm, "higher drive should extend the rays");
    }

    [Fact]
    public void AlbumPulse_Name_is_Album_Pulse()
    {
        Assert.Equal("Album Pulse", new AlbumPulsePreset().Name);
    }

    // ── Minimal Spectrum ──────────────────────────────────────────────────

    [Theory]
    [InlineData(32, 64, 0, 0)]
    [InlineData(32, 64, 31, 62)]
    [InlineData(32, 64, 16, 32)]
    [InlineData(32, 32, 16, 16)]
    public void MinimalSpectrum_SourceIndexForBar_covers_spectrum_proportionally(int bars, int spectrum, int bar, int expected)
    {
        Assert.Equal(expected, MinimalSpectrumPreset.SourceIndexForBar(bars, spectrum, bar));
    }

    [Theory]
    [InlineData(0, 64, 3)]
    [InlineData(32, 0, 3)]
    [InlineData(32, 64, -1)]
    public void MinimalSpectrum_SourceIndexForBar_guards_bad_input(int bars, int spectrum, int bar)
    {
        Assert.Equal(0, MinimalSpectrumPreset.SourceIndexForBar(bars, spectrum, bar));
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(-1f, 0f)]       // negative clamps to 0
    [InlineData(0.5f, 0.3875f)] // 0.55*0.5 + 0.45*0.25
    [InlineData(1f, 1f)]        // 0.55 + 0.45 = 1
    [InlineData(2f, 1f)]        // above 1 clamps to 1
    public void MinimalSpectrum_CurveEnergy_clamps_and_boosts_lower_bands(float raw, float expected)
    {
        Assert.Equal(expected, MinimalSpectrumPreset.CurveEnergy(raw), 3);
    }

    [Fact]
    public void MinimalSpectrum_Name_is_Minimal_Spectrum()
    {
        Assert.Equal("Minimal Spectrum", new MinimalSpectrumPreset().Name);
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private static AudioAnalysisFrame MakeFrame(
        float rms = 0f,
        float transient = 0f,
        float bass = 0f,
        float mid = 0f,
        float high = 0f,
        float peak = 0f)
        => new()
        {
            SampleRate = 44100,
            ChannelCount = 2,
            Spectrum = [0f, 0f, 0f],
            Rms = rms,
            Peak = peak,
            BandEnergy = new Dictionary<FrequencyRange, float>
            {
                [FrequencyRange.Bass] = bass,
                [FrequencyRange.Mid] = mid,
                [FrequencyRange.High] = high,
            },
            TransientEnergy = transient,
        };
}
