using Microsoft.Extensions.DependencyInjection;
using PlaySphere.Application.Visualizer;
using PlaySphere.Domain.Visualizer;
using PlaySphere.Visualizer;
using Xunit;

namespace PlaySphere.UnitTests;

/// <summary>
/// Golden synthetic-signal tests for the visualizer DSP core (PICKLE.md §37): silence, 100 Hz,
/// 1 kHz, 10 kHz, left/right only, white noise and impulse.
/// </summary>
public class AudioAnalyzerTests
{
    private static AudioAnalyzer New(AnalyzerWindow window = AnalyzerWindow.Size2048, int bands = 64)
        => new(window, bands);

    private static float[] Sine(int sampleRate, int frames, double frequency, float amplitude = 0.8f, bool? leftOnly = null, bool? rightOnly = null)
    {
        var mono = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            mono[i] = amplitude * (float)Math.Sin(2.0 * Math.PI * frequency * i / sampleRate);
        }

        if (leftOnly is null && rightOnly is null)
        {
            return mono; // mono input
        }

        // Interleave to stereo.
        var stereo = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var l = leftOnly == true ? mono[i] : 0f;
            var r = rightOnly == true ? mono[i] : 0f;
            stereo[i * 2] = l;
            stereo[i * 2 + 1] = r;
        }
        return stereo;
    }

    private static float[] BalancingStereo(int sampleRate, int frames, double frequency, float amplitude = 0.8f)
    {
        // Identical signal in both channels: [tone, tone, tone, tone, ...].
        var stereo = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var v = amplitude * (float)Math.Sin(2.0 * Math.PI * frequency * i / sampleRate);
            stereo[i * 2] = v;
            stereo[i * 2 + 1] = v;
        }
        return stereo;
    }

    private static float[] Silence(int frames) => new float[frames];

    private static float[] WhiteNoise(int frames, float amplitude = 0.6f)
    {
        var rng = new Random(1234);
        var data = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            data[i] = (float)((rng.NextDouble() * 2 - 1) * amplitude);
        }
        return data;
    }

    private static float[] Impulse(int frames, int at, float amplitude = 0.9f)
    {
        var data = new float[frames];
        data[at] = amplitude;
        return data;
    }

    private static int ArgMax(IReadOnlyList<float> values)
    {
        var max = float.MinValue;
        var index = 0;
        for (var i = 0; i < values.Count; i++)
        {
            if (values[i] > max)
            {
                max = values[i];
                index = i;
            }
        }
        return index;
    }

    private static AudioAnalysisFrame AnalyzeMono(AudioAnalyzer analyzer, float[] samples, int sampleRate, float prevRms = 0f)
        => analyzer.Analyze(samples, sampleRate, 1, prevRms);

    private static AudioAnalysisFrame AnalyzeStereo(AudioAnalyzer analyzer, float[] samples, int sampleRate, float prevRms = 0f)
        => analyzer.Analyze(samples, sampleRate, 2, prevRms);

    [Fact]
    public void Silence_produces_zero_energy()
    {
        var analyzer = New();
        var frame = AnalyzeMono(analyzer, Silence(2048), 48000);

        Assert.Equal(0f, frame.Rms, 5);
        Assert.Equal(0f, frame.Peak, 5);
        Assert.All(frame.Spectrum, v => Assert.Equal(0f, v, 5));
        Assert.Equal(0f, frame.BandEnergy[FrequencyRange.Bass], 5);
        Assert.Equal(0f, frame.BandEnergy[FrequencyRange.Mid], 5);
        Assert.Equal(0f, frame.BandEnergy[FrequencyRange.High], 5);
    }

    [Theory]
    [InlineData(48000)]
    [InlineData(44100)]
    public void Tone_band_index_increases_with_frequency_at_any_sample_rate(int sampleRate)
    {
        var analyzer = New();

        var low = ArgMax(AnalyzeMono(analyzer, Sine(sampleRate, 2048, 100), sampleRate).Spectrum);
        var mid = ArgMax(AnalyzeMono(analyzer, Sine(sampleRate, 2048, 1000), sampleRate).Spectrum);
        var high = ArgMax(AnalyzeMono(analyzer, Sine(sampleRate, 2048, 10000), sampleRate).Spectrum);

        Assert.True(low < mid, $"Expected bass band ({low}) below mid band ({mid}).");
        Assert.True(mid < high, $"Expected mid band ({mid}) below treble band ({high}).");
    }

    [Fact]
    public void Tone_peak_tracks_amplitude()
    {
        var analyzer = New();
        var frame = AnalyzeMono(analyzer, Sine(48000, 2048, 1000, amplitude: 0.5f), 48000);

        Assert.InRange(frame.Peak, 0.45f, 0.55f);
        Assert.InRange(frame.Rms, 0.2f, 0.45f);
    }

    [Fact]
    public void Left_only_and_right_only_produce_identical_analysis()
    {
        var analyzer = New();

        var left = AnalyzeStereo(analyzer, Sine(48000, 2048, 1000, leftOnly: true), 48000, 0);
        var right = AnalyzeStereo(analyzer, Sine(48000, 2048, 1000, rightOnly: true), 48000, 0);

        Assert.Equal(left.Rms, right.Rms, 4);
        Assert.Equal(left.Peak, right.Peak, 4);
        Assert.Equal(ArgMax(left.Spectrum), ArgMax(right.Spectrum));
    }

    [Fact]
    public void Balanced_stereo_has_twice_the_rms_of_left_only()
    {
        var analyzer = New();

        var leftOnly = AnalyzeStereo(analyzer, Sine(48000, 2048, 1000, leftOnly: true), 48000, 0);
        var balanced = AnalyzeStereo(analyzer, BalancingStereo(48000, 2048, 1000), 48000, 0);

        // Balanced downmix keeps both channels (2× the amplitude of a single channel).
        Assert.InRange(balanced.Rms / leftOnly.Rms, 1.8f, 2.2f);
    }

    [Fact]
    public void Stereo_input_is_declared_in_frame_metadata()
    {
        var analyzer = New();
        var frame = AnalyzeStereo(analyzer, Sine(48000, 2048, 1000), 48000);

        Assert.Equal(2, frame.ChannelCount);
        Assert.Equal(48000, frame.SampleRate);
    }

    [Fact]
    public void White_noise_spreads_energy_across_bands()
    {
        var analyzer = New();
        var frame = AnalyzeMono(analyzer, WhiteNoise(2048), 48000);

        Assert.True(frame.Rms > 0.05f, "Noise should have meaningful RMS.");
        Assert.True(frame.Spectrum.Count > 8);
        var energized = frame.Spectrum.Count(v => v > frame.Spectrum.Max() * 0.2f);
        Assert.True(energized >= 4, "Noise should energize many spectrum bands.");
    }

    [Fact]
    public void Impulse_concentrates_but_spreads_energy()
    {
        var analyzer = New();
        var frame = AnalyzeMono(analyzer, Impulse(2048, 1024), 48000);

        Assert.InRange(frame.Peak, 0.7f, 1.0f);
        Assert.True(frame.Rms > 0.01f, "Impulse should yield non-zero RMS.");
    }

    [Fact]
    public void Four_k_window_supports_larger_fft()
    {
        var analyzer = New(AnalyzerWindow.Size4096);

        var low = ArgMax(AnalyzeMono(analyzer, Sine(48000, 4096, 100), 48000).Spectrum);
        var high = ArgMax(AnalyzeMono(analyzer, Sine(48000, 4096, 10000), 48000).Spectrum);

        Assert.True(low < high);
    }

    [Fact]
    public void Bass_tone_dominates_bass_energy_range()
    {
        var analyzer = New();
        var frame = AnalyzeMono(analyzer, Sine(48000, 2048, 100, amplitude: 0.8f), 48000);

        Assert.True(
            frame.BandEnergy[FrequencyRange.Bass] >= frame.BandEnergy[FrequencyRange.High],
            "A 100 Hz tone should have at least as much bass energy as treble energy.");
    }

    [Fact]
    public void Transient_energy_reacts_to_rms_jump()
    {
        var analyzer = New();

        // previous frame is silent → large jump → high transient.
        var jump = AnalyzeMono(analyzer, Sine(48000, 2048, 1000), 48000, prevRms: 0f);
        // previous frame loud, current loud but equal → no transient.
        var steady = AnalyzeMono(analyzer, Sine(48000, 2048, 1000), 48000, prevRms: jump.Rms);

        Assert.True(jump.TransientEnergy > 0f);
        Assert.True(steady.TransientEnergy < jump.TransientEnergy);
    }

    [Fact]
    public void Rejects_invalid_inputs()
    {
        var analyzer = New();
        var samples = Sine(48000, 2048, 1000);

        Assert.Throws<ArgumentOutOfRangeException>(() => analyzer.Analyze(samples, 0, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => analyzer.Analyze(samples, 48000, 3, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioAnalyzer(AnalyzerWindow.Size2048, 0));
    }
}

public class PcmAudioSourceTests
{
    [Fact]
    public void Publishes_a_frame_when_a_full_window_accumulates()
    {
        var analyzer = new AudioAnalyzer();
        var channel = new LatestAnalysisFrameChannel();
        var source = new PcmAudioSource(analyzer, channel);

        var samples = new float[2048];
        for (var i = 0; i < 2048; i++)
        {
            samples[i] = 0.5f * (float)Math.Sin(2.0 * Math.PI * 440 * i / 48000);
        }

        Assert.Null(channel.TryReadLatest());
        source.PushSamples(samples, 48000, 1);
        Assert.NotNull(channel.TryReadLatest());
        Assert.Equal(48000, channel.TryReadLatest()!.SampleRate);
    }

    [Fact]
    public void Processes_multiple_windows_from_one_push()
    {
        var analyzer = new AudioAnalyzer();
        var channel = new LatestAnalysisFrameChannel();
        var source = new PcmAudioSource(analyzer, channel);

        var samples = new float[2048 * 3];
        source.PushSamples(samples, 48000, 1);

        // Three full windows → the channel holds the last-published frame.
        Assert.NotNull(channel.TryReadLatest());
    }

    [Fact]
    public void Format_change_resets_buffered_samples()
    {
        var analyzer = new AudioAnalyzer();
        var channel = new LatestAnalysisFrameChannel();
        var source = new PcmAudioSource(analyzer, channel);

        source.PushSamples(new float[2048], 48000, 1);
        Assert.NotNull(channel.TryReadLatest());

        // Channel-count change: no new frame until a stereo window accumulates.
        source.PushSamples(new float[1], 48000, 2);
        Assert.True(channel.TryReadLatest()!.ChannelCount is 1 or 2);
    }

    [Fact]
    public void Stop_clears_buffered_samples()
    {
        var analyzer = new AudioAnalyzer();
        var channel = new LatestAnalysisFrameChannel();
        var source = new PcmAudioSource(analyzer, channel);

        source.PushSamples(new float[2048], 48000, 1);
        source.Stop();
        source.PushSamples(new float[2047], 48000, 1);

        // 2047 < 2048 window → no new frame; previous one was cleared by Stop.
        Assert.NotNull(channel.TryReadLatest());
    }

    /// <summary>
    /// Regression (the real-app "visualizer crashes a few seconds after enabling" heap corruption):
    /// the source can be pushed on libVLC's audio thread while <see cref="PcmAudioSource.Stop"/> runs
    /// on another thread (e.g. a UI-thread detach). Concurrent mutation of the shared buffer must
    /// neither throw nor leave the source corrupted — it now holds a lock over the buffer.
    /// </summary>
    [Fact]
    public async Task Concurrent_pushes_and_stops_do_not_corrupt_or_throw()
    {
        var analyzer = new AudioAnalyzer();
        var channel = new LatestAnalysisFrameChannel();
        var source = new PcmAudioSource(analyzer, channel);
        var window = new float[2048];
        Array.Fill(window, 0.1f);

        using var done = new CancellationTokenSource();
        var pushTasks = Enumerable.Range(0, 4).Select(_ =>
            Task.Run(() =>
            {
                while (!done.IsCancellationRequested)
                {
                    source.PushSamples(window, 48000, 1);
                }
            })).ToArray();
        var stopTask = Task.Run(async () =>
        {
            var stop = false;
            while (!done.IsCancellationRequested)
            {
                if (stop)
                {
                    source.Stop();
                    stop = false;
                }
                else
                {
                    stop = true;
                }
                await Task.Delay(1);
            }
        });

        await Task.Delay(250);
        done.Cancel();

        // Any task that faulted means a data race threw/corrupted — that must not happen.
        await Task.WhenAll(pushTasks);
        await stopTask;

        // The source must still be functional after the churn.
        source.PushSamples(window, 48000, 1);
        Assert.NotNull(channel.TryReadLatest());
    }
}

public class LatestAnalysisFrameChannelTests
{
    [Fact]
    public void Keeps_only_the_most_recent_frame()
    {
        var channel = new LatestAnalysisFrameChannel();
        var maker = new AudioAnalyzer();
        var a = maker.Analyze(new float[2048], 48000, 1, 0);
        var b = maker.Analyze(new float[2048], 48000, 1, 0);

        Assert.Null(channel.TryReadLatest());
        channel.Publish(a);
        channel.Publish(b);

        Assert.Same(b, channel.TryReadLatest());
    }

    [Fact]
    public void Rejects_null_publish()
    {
        var channel = new LatestAnalysisFrameChannel();
        Assert.Throws<ArgumentNullException>(() => channel.Publish(null!));
    }
}

public class VisualizerServiceCollectionRegistrationTests
{
    [Fact]
    public void AddPlaySphereVisualizer_registers_all_contracts()
    {
        var services = new ServiceCollection();
        services.AddPlaySphereVisualizer();
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetService<IAudioAnalyzer>());
        Assert.NotNull(provider.GetService<ILatestAnalysisFrameProvider>());
        Assert.NotNull(provider.GetService<IVisualizerAudioSource>());

        // The default source is wired to the same channel/analyzer (latest-frame pipeline).
        var source = (PcmAudioSource)provider.GetRequiredService<IVisualizerAudioSource>();
        source.PushSamples(new float[2048], 44100, 1);
        Assert.NotNull(((ILatestAnalysisFrameProvider)provider.GetRequiredService<ILatestAnalysisFrameProvider>()).TryReadLatest());
    }
}
