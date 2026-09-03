using PlaySphere.Application.Visualizer;
using PlaySphere.Domain.Visualizer;

namespace PlaySphere.Visualizer;

/// <summary>
/// Implements <see cref="IVisualizerAudioSource"/>: accumulates decoded interleaved float PCM and,
/// whenever a full analysis window has accumulated, runs the <see cref="IAudioAnalyzer"/> and
/// publishes the resulting <see cref="AudioAnalysisFrame"/> to
/// <see cref="LatestAnalysisFrameChannel"/>. A stream format change (sample rate or channel count)
/// resets the buffer. The analyser and channel (not this buffer) bound how much work each audio
/// callback does.
/// </summary>
public sealed class PcmAudioSource : IVisualizerAudioSource
{
    private readonly IAudioAnalyzer _analyzer;
    private readonly ILatestAnalysisFrameProvider _sink;
    private readonly int _windowFrames;
    private readonly object _gate = new();
    private readonly List<float> _buffer = new();
    private int _sampleRate;
    private int _channelCount;
    private float _lastRms;

    /// <summary>
    /// Creates a PCM source that publishes analysis frames to <paramref name="sink"/> every
    /// <see cref="AnalyzerWindow"/> frames.
    /// </summary>
    public PcmAudioSource(IAudioAnalyzer analyzer, ILatestAnalysisFrameProvider sink, AnalyzerWindow window = AnalyzerWindow.Size2048)
    {
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _windowFrames = (int)window;
    }

    /// <inheritdoc />
    public void Start()
    {
        // Decoder-fed source: samples arrive via PushSamples, so there is nothing to start.
    }

    /// <inheritdoc />
    public void PushSamples(ReadOnlySpan<float> samples, int sampleRate, int channelCount)
    {
        if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
        if (channelCount is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(channelCount));

        // The buffer is shared between the libVLC audio thread (PushSamples) and any thread that
        // calls Stop() (e.g. a detach from the UI thread) — serialize access so a concurrent
        // Add / RemoveRange cannot corrupt the heap (which previously surfaced as a late native AV).
        lock (_gate)
        {
            if (sampleRate != _sampleRate || channelCount != _channelCount)
            {
                _buffer.Clear();
                _sampleRate = sampleRate;
                _channelCount = channelCount;
            }

            for (var i = 0; i < samples.Length; i++)
            {
                _buffer.Add(samples[i]);
            }

            DrainWindowsLocked();
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_gate)
        {
            _buffer.Clear();
            _lastRms = 0f;
        }
    }

    private void DrainWindowsLocked()
    {
        var needed = _windowFrames * _channelCount;
        while (_buffer.Count >= needed)
        {
            // Analyze the oldest full window (interleaved, in-buffer order). The lock is held, so the
            // analyzer/publish run while the buffer is stable; publishing only replaces the latest
            // frame (volatile write) and never touches this buffer.
            ReadOnlySpan<float> span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_buffer);
            var frame = _analyzer.Analyze(span[..needed], _sampleRate, _channelCount, _lastRms);
            _sink.Publish(frame);
            _lastRms = frame.Rms;
            _buffer.RemoveRange(0, needed);
        }
    }
}
