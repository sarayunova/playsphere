using System.Numerics;
using PlaySphere.Application.Visualizer;
using PlaySphere.Domain.Visualizer;
using PlaySphere.Visualizer.Dsp;

namespace PlaySphere.Visualizer;

/// <summary>
/// Default <see cref="IAudioAnalyzer"/>: Hann-windowed FFT (2048/4096) with logarithmic
/// frequency mapping, plus RMS, peak, bass/mid/high band energy and transient energy
/// (PICKLE.md §33–§34). Pure DSP — no rendering, no I/O, no allocation per analysis window
/// after construction.
/// </summary>
public sealed class AudioAnalyzer : IAudioAnalyzer
{
    private readonly int _windowSize;
    private readonly int _bandCount;
    private readonly float[] _hann;
    private readonly Complex[] _fftBuffer;
    private readonly float[] _magnitude;
    private readonly float[] _spectrum;
    private readonly Fft _fft;
    private readonly Dictionary<int, (int Start, int End)[]> _bandRangesByRate = new();

    /// <summary>Smallest frequency (Hz) mapped to the first spectrum band.</summary>
    public const double LowFrequencyHz = 20.0;

    /// <summary>
    /// Creates an analyser.
    /// </summary>
    /// <param name="window">FFT window size (2048 or 4096).</param>
    /// <param name="bandCount">Number of log-mapped spectrum bands (e.g. 64).</param>
    public AudioAnalyzer(AnalyzerWindow window = AnalyzerWindow.Size2048, int bandCount = 64)
    {
        if (bandCount < 1) throw new ArgumentOutOfRangeException(nameof(bandCount), "Need at least one band.");

        _windowSize = (int)window;
        _bandCount = bandCount;
        _hann = new float[_windowSize];
        _fftBuffer = new Complex[_windowSize];
        _magnitude = new float[_windowSize / 2];
        _spectrum = new float[bandCount];
        _fft = new Fft(_windowSize);

        for (var i = 0; i < _windowSize; i++)
        {
            _hann[i] = (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (_windowSize - 1))));
        }
    }

    /// <summary>
    /// Computes frequency-bin index ranges for each log-mapped band, caching them per sample rate
    /// so the analyser honours the actual input sample rate (never assumes 44.1 kHz — §34). Only
    /// the sample rates actually seen (typically 44.1 k and 48 k) are cached.
    /// </summary>
    private (int Start, int End)[] GetBandRanges(int sampleRate)
    {
        if (_bandRangesByRate.TryGetValue(sampleRate, out var cached))
        {
            return cached;
        }

        var nyquist = sampleRate / 2.0;
        var minLog = Math.Log(LowFrequencyHz);
        var maxLog = Math.Log(nyquist);
        var binHz = sampleRate / (double)_windowSize;

        var ranges = new (int Start, int End)[_bandCount];
        var lastBin = 0;
        for (var b = 0; b < _bandCount; b++)
        {
            var lo = Math.Exp(minLog + (maxLog - minLog) * (b / (double)_bandCount));
            var hi = Math.Exp(minLog + (maxLog - minLog) * ((b + 1) / (double)_bandCount));
            var startBin = Math.Max(lastBin, (int)Math.Round(lo / binHz));
            var endBin = Math.Clamp((int)Math.Round(hi / binHz), 0, _windowSize / 2 - 1);
            if (endBin <= startBin) endBin = startBin + 1;
            ranges[b] = (startBin, Math.Min(endBin, _windowSize / 2 - 1));
            lastBin = endBin;
        }

        _bandRangesByRate[sampleRate] = ranges;
        return ranges;
    }

    /// <inheritdoc />
    public AudioAnalysisFrame Analyze(ReadOnlySpan<float> samples, int sampleRate, int channelCount, float previousRms)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channelCount is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(channelCount), "Only mono or stereo are supported.");

        // Downmix to mono (average across channels) into a Hann-windowed FFT buffer.
        var framesAvailable = sampleRate == 0 ? 0 : samples.Length / channelCount;
        var fill = Math.Min(_windowSize, framesAvailable);
        for (var i = 0; i < fill; i++)
        {
            var sample = DefaultIfMissing(samples, i * channelCount, channelCount);
            if (channelCount == 2)
            {
                sample = (sample + DefaultIfMissing(samples, i * channelCount + 1, channelCount)) * 0.5f;
            }
            _fftBuffer[i] = new Complex(sample * _hann[i], 0);
        }
        for (var i = fill; i < _windowSize; i++)
        {
            _fftBuffer[i] = Complex.Zero;
        }

        // RMS + peak over the downmixed window.
        var sumSquares = 0.0;
        var peak = 0.0f;
        for (var i = 0; i < _windowSize; i++)
        {
            var v = (float)_fftBuffer[i].Real; // already windowed mono
            sumSquares += v * v;
            var a = Math.Abs(v);
            if (a > peak) peak = (float)a;
        }
        var rms = (float)Math.Sqrt(sumSquares / _windowSize);
        var peakNormalized = (float)Math.Min(1.0, peak);

        // Spectrum magnitudes.
        _fft.Transform(_fftBuffer);
        for (var i = 0; i < _windowSize / 2; i++)
        {
            _magnitude[i] = (float)Math.Sqrt(_fftBuffer[i].Magnitude);
        }

        // Log-mapped spectrum bands.
        var bandRanges = GetBandRanges(sampleRate);
        var spectrum = _spectrum;
        for (var b = 0; b < _bandCount; b++)
        {
            var (start, end) = bandRanges[b];
            var sum = 0.0;
            for (var i = start; i <= end; i++)
            {
                sum += _magnitude[i];
            }
            spectrum[b] = start <= end ? (float)(sum / (end - start + 1)) : 0f;
        }

        var bandEnergy = ComputeBandEnergy(spectrum);
        var transient = ComputeTransient(rms, previousRms);

        return new AudioAnalysisFrame
        {
            SampleRate = sampleRate,
            ChannelCount = channelCount,
            Spectrum = spectrum.ToArray(),
            Rms = rms,
            Peak = peakNormalized,
            BandEnergy = bandEnergy,
            TransientEnergy = transient,
        };
    }

    private static float DefaultIfMissing(ReadOnlySpan<float> samples, int index, int channelCount)
    {
        // index always valid from fill computation; defensive to avoid OOB on ragged tail.
        if (index < samples.Length) return samples[index];
        return 0f;
    }

    private static IReadOnlyDictionary<FrequencyRange, float> ComputeBandEnergy(float[] spectrum)
    {
        // Weight spectrum thirds into bass/mid/high. Bands ascend from low to high frequency.
        var third = spectrum.Length / 3;
        if (third < 1) third = 1;

        float Bass() { var s = 0f; for (var i = 0; i < third && i < spectrum.Length; i++) s += spectrum[i]; return s / third; }
        float Mid() { var s = 0f; var c = 0; for (var i = third; i < third * 2 && i < spectrum.Length; i++) { s += spectrum[i]; c++; } return c == 0 ? 0f : s / c; }
        float High() { var s = 0f; var c = 0; for (var i = third * 2; i < spectrum.Length; i++) { s += spectrum[i]; c++; } return c == 0 ? 0f : s / c; }

        var bass = Bass();
        var mid = Mid();
        var high = High();
        var max = Math.Max(bass, Math.Max(mid, high));
        if (max <= 0f)
        {
            return new Dictionary<FrequencyRange, float>
            {
                [FrequencyRange.Bass] = 0f,
                [FrequencyRange.Mid] = 0f,
                [FrequencyRange.High] = 0f,
            };
        }

        return new Dictionary<FrequencyRange, float>
        {
            [FrequencyRange.Bass] = Math.Min(1f, bass / max),
            [FrequencyRange.Mid] = Math.Min(1f, mid / max),
            [FrequencyRange.High] = Math.Min(1f, high / max),
        };
    }

    private static float ComputeTransient(float rms, float previousRms)
    {
        // A jump from (near) silence is a strong transient: a non-zero current RMS against a
        // vanishing previous RMS yields ~1. A steady state yields ~0.
        var denom = Math.Max(previousRms, 1e-4f);
        var delta = (rms - previousRms) / denom;
        return Math.Clamp(delta, 0f, 1f);
    }
}
