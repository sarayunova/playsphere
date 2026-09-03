using NAudio.Wave;
using PlaySphere.Audio;
using Xunit;

namespace PlaySphere.UnitTests;

/// <summary>
/// Pure, headless tests for <see cref="WasapiLoopbackPcmConverter"/> — the byte→float PCM conversion
/// and silence synthesis used by the WASAPI device-loopback visualizer source (ADR-0009). The actual
/// NAudio recorder/loopback capture can't be exercised without a real Windows audio stack, so this is
/// the verifiable slice; the capture wiring itself is covered by the interactive manual pass.
/// </summary>
public sealed class WasapiLoopbackPcmConverterTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(0.5f)]
    [InlineData(-0.25f)]
    [InlineData(0.999f)]
    [InlineData(-1f)]
    public void IeeeFloat_bytes_convert_to_equal_float_samples(float value)
    {
        byte[] bytes = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(bytes, value);

        var result = WasapiLoopbackPcmConverter.ConvertToFl32(bytes, 48000, 2, WaveFormatEncoding.IeeeFloat, 32);

        Assert.Equal(1, result.Count);
        Assert.Equal(value, result.Samples[0], precision: 5);
    }

    [Fact]
    public void IeeeFloat_stereo_interleaved_preserves_order_and_count()
    {
        Span<float> input = stackalloc float[4] { 0.1f, -0.2f, 0.3f, -0.4f };
        var bytes = new byte[4 * 4];
        for (var i = 0; i < input.Length; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4, 4), input[i]);
        }

        var result = WasapiLoopbackPcmConverter.ConvertToFl32(bytes, 44100, 2, WaveFormatEncoding.IeeeFloat, 32);

        Assert.Equal(4, result.Count);
        for (var i = 0; i < input.Length; i++)
        {
            Assert.Equal(input[i], result.Samples[i], precision: 5);
        }
    }

    [Theory]
    [InlineData(32767, 0.999969f)]
    [InlineData(16384, 0.5f)]
    [InlineData(-16384, -0.5f)]
    [InlineData(-32768, -1f)]
    [InlineData(0, 0f)]
    public void Pcm16_bytes_convert_to_normalized_float(short raw, float expected)
    {
        byte[] bytes = { (byte)(raw & 0xFF), (byte)((raw >> 8) & 0xFF) };

        var result = WasapiLoopbackPcmConverter.ConvertToFl32(bytes, 48000, 1, WaveFormatEncoding.Pcm, 16);

        Assert.Equal(1, result.Count);
        Assert.Equal(expected, result.Samples[0], precision: 4);
    }

    [Fact]
    public void Extensible_16bit_pcm_converts_as_pcm()
    {
        byte[] bytes = { 0x00, 0x80 }; // -32768

        var result = WasapiLoopbackPcmConverter.ConvertToFl32(bytes, 44100, 2, WaveFormatEncoding.Extensible, 16);

        Assert.Equal(1, result.Count);
        Assert.Equal(-1f, result.Samples[0], precision: 4);
    }

    [Fact]
    public void Extensible_32bit_is_treated_as_ieee_float()
    {
        // The real device mix format on this machine is Extensible IEEE_FLOAT 48000 32-bit; that must
        // decode as float, not as reinterpreted 16-bit PCM.
        byte[] bytes = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(bytes, 0.5f);

        var result = WasapiLoopbackPcmConverter.ConvertToFl32(bytes, 48000, 2, WaveFormatEncoding.Extensible, 32);

        Assert.Equal(1, result.Count);
        Assert.Equal(0.5f, result.Samples[0], precision: 5);
    }

    [Fact]
    public void Unsupported_encoding_returns_zero()
    {
        byte[] bytes = { 1, 2, 3, 4 };

        var result = WasapiLoopbackPcmConverter.ConvertToFl32(bytes, 44100, 2, WaveFormatEncoding.Adpcm, 16);

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void Unsupported_bit_depth_returns_zero()
    {
        byte[] bytes = { 1, 2, 3, 4 };

        var result = WasapiLoopbackPcmConverter.ConvertToFl32(bytes, 44100, 2, WaveFormatEncoding.Pcm, 5);

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void Empty_src_returns_zero()
    {
        var result = WasapiLoopbackPcmConverter.ConvertToFl32(ReadOnlySpan<byte>.Empty, 44100, 2, WaveFormatEncoding.IeeeFloat, 32);

        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void CreateSilence_returns_all_zeros_of_requested_length()
    {
        var result = WasapiLoopbackPcmConverter.CreateSilence(44100, 2);

        Assert.Equal(44100 * 2, result.Count);
        Assert.All(result.Samples, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void CreateSilence_rejects_invalid_format()
    {
        Assert.Equal(0, WasapiLoopbackPcmConverter.CreateSilence(0, 2).Count);
        Assert.Equal(0, WasapiLoopbackPcmConverter.CreateSilence(44100, 3).Count);
        Assert.Equal(0, WasapiLoopbackPcmConverter.CreateSilence(44100, 0).Count);
    }

    [Fact]
    public void IeeeFloat_bounds_output_to_destination_capacity()
    {
        var bytes = new byte[4 * 8];
        for (var i = 0; i < 8; i++)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4, 4), 1f);
        }
        var dest = new float[3];

        var count = WasapiLoopbackPcmConverter.ConvertToFl32(bytes, 48000, 2, WaveFormatEncoding.IeeeFloat, 32, dest);

        Assert.Equal(3, count);
        Assert.All(dest, s => Assert.Equal(1f, s));
    }
}