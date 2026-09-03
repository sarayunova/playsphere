using System.Numerics;

namespace PlaySphere.Visualizer.Dsp;

/// <summary>
/// In-place radix-2 Cooley-Tukey FFT for power-of-two sizes (2048/4096, PICKLE.md §34).
/// Pure DSP, no external dependencies. Precomputes bit-reversal permutation and twiddle
/// factors once per instance so repeated analysis is allocation-free after construction.
/// </summary>
internal sealed class Fft
{
    private readonly int _size;
    private readonly int[] _reversed;
    private readonly Complex[] _twiddles;

    public Fft(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "FFT size must be a power of two.");
        }

        _size = size;
        _reversed = new int[size];
        _twiddles = new Complex[size];

        var bits = (int)Math.Log2(size);
        for (var i = 0; i < size; i++)
        {
            _reversed[i] = ReverseBits(i, bits);
        }

        for (var i = 0; i < size; i++)
        {
            var angle = -2.0 * Math.PI * i / size;
            _twiddles[i] = new Complex(Math.Cos(angle), Math.Sin(angle));
        }
    }

    private static int ReverseBits(int value, int bits)
    {
        var result = 0;
        for (var i = 0; i < bits; i++)
        {
            result = (result << 1) | (value & 1);
            value >>= 1;
        }
        return result;
    }

    /// <summary>
    /// Transforms <paramref name="data"/> in place. The complex spectrum on return uses
    /// <c>data[i]</c> for frequency bin <c>i</c>; bins above <c>size/2</c> are the conjugate mirror.
    /// </summary>
    public void Transform(Span<Complex> data)
    {
        if (data.Length != _size)
        {
            throw new ArgumentException($"FFT expects {_size} samples.", nameof(data));
        }

        for (var i = 0; i < _size; i++)
        {
            var j = _reversed[i];
            if (j > i)
            {
                (data[i], data[j]) = (data[j], data[i]);
            }
        }

        for (var len = 2; len <= _size; len <<= 1)
        {
            var half = len >> 1;
            var step = _size / len;
            for (var i = 0; i < _size; i += len)
            {
                for (var k = 0; k < half; k++)
                {
                    var twiddle = _twiddles[k * step];
                    var even = data[i + k];
                    var odd = data[i + k + half] * twiddle;
                    data[i + k] = even + odd;
                    data[i + k + half] = even - odd;
                }
            }
        }
    }
}
