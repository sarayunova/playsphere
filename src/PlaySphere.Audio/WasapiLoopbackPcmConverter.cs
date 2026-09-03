using System;
using NAudio.Wave;

namespace PlaySphere.Audio;

/// <summary>
/// Pure, headless-testable helpers for converting WASAPI loopback byte buffers to interleaved float
/// PCM (the format <see cref="PlaySphere.Application.Visualizer.IVisualizerAudioSource"/> expects,
/// range roughly −1..1) and for synthesizing silent frames. All logic is allocation-free for the
/// conversion path (writes into a caller-provided buffer).
/// </summary>
public static class WasapiLoopbackPcmConverter
{
    /// <summary>
    /// Converts a captured WASAPI buffer to interleaved float PCM into <paramref name="dest"/>.
    /// Supports 32-bit IEEE float (<see cref="WaveFormatEncoding.IeeeFloat"/>) and integer PCM
    /// (<see cref="WaveFormatEncoding.Pcm"/>, <see cref="WaveFormatEncoding.Extensible"/>) at the
    /// given bit depth. <paramref name="bitsPerSample"/> is taken from the real capture
    /// <c>WaveFormat.BitsPerSample</c> (a WASAPI loopback mix format is often Extensible with 32-bit
    /// IEEE float, sometimes 16-bit PCM — only inspecting the bit depth disambiguates, because
    /// <see cref="WaveFormatEncoding.Extensible"/> reports the same encoding for both). Returns the
    /// number of float samples written, or 0 when the encoding/bit-depth is unsupported.
    /// </summary>
    public static int ConvertToFl32(ReadOnlySpan<byte> src, int sampleRate, int channels, WaveFormatEncoding encoding, int bitsPerSample, Span<float> dest)
    {
        if (src.IsEmpty || dest.IsEmpty || sampleRate <= 0 || channels is < 1 or > 2)
        {
            return 0;
        }

        var isFloat = encoding == WaveFormatEncoding.IeeeFloat
                      || (encoding == WaveFormatEncoding.Extensible && bitsPerSample == 32);
        if (isFloat)
        {
            var floated = src.Length / 4;
            var count = Math.Min(floated, dest.Length);
            for (var i = 0; i < count; i++)
            {
                dest[i] = System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian(src.Slice(i * 4, 4));
            }
            return count;
        }

        if (encoding == WaveFormatEncoding.Pcm || encoding == WaveFormatEncoding.Extensible)
        {
            var bytesPerSample = bitsPerSample switch
            {
                8 => 1,
                16 => 2,
                24 => 3,
                32 => 4,
                _ => 0,
            };

            if (bytesPerSample == 0)
            {
                return 0;
            }

            var sampleCount = src.Length / bytesPerSample;
            var count = Math.Min(sampleCount, dest.Length);
            for (var i = 0; i < count; i++)
            {
                var slice = src.Slice(i * bytesPerSample, bytesPerSample);
                dest[i] = bytesPerSample switch
                {
                    1 => (sbyte)slice[0] / 128f,
                    2 => System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(slice) / 32768f,
                    3 => ReadInt24Scaled(slice),
                    4 => System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(slice) / 2147483648f,
                    _ => 0f,
                };
            }
            return count;
        }

        return 0;
    }

    private static float ReadInt24Scaled(ReadOnlySpan<byte> slice)
    {
        // Signed 24-bit little-endian → normalize to −1..1 (sign-extend to 32 bits then scale).
        int v = (slice[0] & 0xFF) | ((slice[1] & 0xFF) << 8) | ((slice[2] & 0xFF) << 16);
        if ((v & 0x800000) != 0)
        {
            v |= unchecked((int)0xFF000000);
        }
        return v / 8388608f;
    }

    /// <summary>
    /// Convenience overload returning the converted samples in a freshly allocated array along with
    /// the number of valid samples (for the capture callback where no reusable buffer is maintained).
    /// </summary>
    public static (float[] Samples, int Count) ConvertToFl32(ReadOnlySpan<byte> src, int sampleRate, int channels, WaveFormatEncoding encoding, int bitsPerSample)
    {
        var isFloat = encoding == WaveFormatEncoding.IeeeFloat
                      || (encoding == WaveFormatEncoding.Extensible && bitsPerSample == 32);
        var bytesPerSample = isFloat ? 4 : (encoding is WaveFormatEncoding.Pcm or WaveFormatEncoding.Extensible ? bitsPerSample / 8 : 0);
        var maxSamples = bytesPerSample == 0 || sampleRate <= 0 || channels is < 1 or > 2
            ? 0
            : src.Length / bytesPerSample;
        var dest = new float[maxSamples];
        var written = ConvertToFl32(src, sampleRate, channels, encoding, bitsPerSample, dest);
        return (dest, written);
    }

    /// <summary>
    /// Creates a one-second silent sample buffer at the given format (for padding idle loopback gaps).
    /// Returns an empty tuple for an unsupported encoding or invalid format.
    /// </summary>
    public static (float[] Samples, int Count) CreateSilence(int sampleRate, int channels)
    {
        if (sampleRate <= 0 || channels is < 1 or > 2)
        {
            return (Array.Empty<float>(), 0);
        }
        var buffer = new float[sampleRate * channels];
        return (buffer, buffer.Length);
    }
}
