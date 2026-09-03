using System;
using System.Diagnostics;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using PlaySphere.Application.Visualizer;

namespace PlaySphere.Audio;

/// <summary>
/// Implements <see cref="IVisualizerAudioSource"/> by capturing the audio Windows mixes to the
/// output endpoint via NAudio 3's WASAPI device-loopback (<c>WasapiRecorderBuilder.WithLoopbackCapture</c>),
/// so the visualizer animates in sync with what the player is actually playing — while libVLC keeps
/// outputting to the speakers normally (ADR-0009). This is the Windows path that delivers
/// "visualizer + audible audio" simultaneously; on non-Windows hosts the app falls back to the
/// ADR-0008 engine PCM tap. A per-process loopback was originally used but did not attach to
/// libVLC's shared-mode render stream on some machines (static visualizer), so the reliable
/// device-whole endpoint loopback is used instead (ADR-0009 revision).
/// </summary>
public sealed class WasapiLoopbackCaptureSource : IVisualizerAudioSource, IDisposable
{
    private readonly IVisualizerAudioSource _inner;
    private readonly Func<Task<WasapiRecorder>> _recorderFactory;
    private readonly int _silencePadMilliseconds;
    private readonly object _gate = new();
    private WasapiRecorder? _recorder;
    private bool _started;
    private System.Threading.Timer? _silenceTimer;
    private int _packetCount;
    private long _sampleCount;
    private double _recentPeak;

    /// <summary>
    /// Creates a loopback source. <paramref name="inner"/> is the actual <see cref="IVisualizerAudioSource"/>
    /// that runs the analyser (e.g. <see cref="PcmAudioSource"/>); this wrapper intercepts the captured
    /// audio and forwards it. <paramref name="recorderFactory"/> produces the NAudio recorder (defaults
    /// to building a device-loopback recorder for the default render endpoint).
    /// </summary>
    public WasapiLoopbackCaptureSource(
        IVisualizerAudioSource inner,
        Func<Task<WasapiRecorder>>? recorderFactory = null,
        int silencePadMilliseconds = 250)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _recorderFactory = recorderFactory ?? BuildDefaultRecorderAsync;
        _silencePadMilliseconds = silencePadMilliseconds;
        _silenceTimer = new System.Threading.Timer(_ => PadSilenceIfIdle(), null, global::System.Threading.Timeout.Infinite, global::System.Threading.Timeout.Infinite);
    }

/// <summary>
/// Builds a NAudio loopback recorder that captures the entire output endpoint (the system's default
/// render device), which reliably delivers whatever Windows mixes to the speakers — including the
/// audio libVLC renders. This is the documented, battle-tested WASAPI loopback path; it supersedes
/// the earlier per-process loopback (<c>WithProcessLoopback</c>), which on some machines/devices does
/// not attach to libVLC's shared-mode render stream and so produced a static visualizer. Trade-off:
/// the endpoint stream includes any other audio Windows mixes to that device (ADR-0009 revision).
/// </summary>
private static Task<WasapiRecorder> BuildDefaultRecorderAsync()
{
    // Device loopback uses the default render endpoint. Synchronous build (as the docs show for
    // WithLoopbackCapture); wrapped as a Task so the caller sees a uniform async factory.
    var recorder = new WasapiRecorderBuilder().WithLoopbackCapture().Build();
    return Task.FromResult(recorder);
}

    /// <summary>
    /// Starts loopback capture. If the recorder hasn't been built yet, builds it (best-effort: any
    /// failure is swallowed and the source stays inactive rather than breaking the player).
    /// </summary>
    public async Task StartAsync()
    {
        WasapiRecorder? recorder;
        lock (_gate)
        {
            if (_started)
            {
                return;
            }
            recorder = _recorder;
        }

        if (recorder == null)
        {
            try
            {
                recorder = await _recorderFactory().ConfigureAwait(false);
                VisualizerAudioTrace.Log($"loopback recorder built OK; WaveFormat={recorder?.WaveFormat}", force: true);
            }
            catch (Exception ex)
            {
                // Loopback capture unavailable (e.g. device gone, permission) — degrade gracefully to
                // an inactive source rather than taking the player down.
                VisualizerAudioTrace.Log($"loopback recorder build FAILED: {ex}", force: true);
                Debug.WriteLine($"WasapiLoopbackCaptureSource build failed: {ex}");
                return;
            }
            lock (_gate)
            {
                if (_started)
                {
                    // A concurrent Start() won the race; discard the recorder we just built.
                    if (recorder != null)
                    {
                        ReleaseRecorder(recorder);
                    }
                    return;
                }
                _recorder = recorder;
            }
        }

        // At this point <c>recorder</c> is definitely non-null (either it was non-null when read under
        // the lock up front, or we just built it and stored it under the lock without returning), but
        // the compiler can't prove that across the await+lock, so capture a non-null local for the rest.
        var r = recorder;
        if (r == null)
        {
            return;
        }

        r.DataAvailable += OnDataAvailable;
        r.RecordingStopped += OnRecordingStopped;
        try
        {
            r.StartRecording();
            lock (_gate)
            {
                _started = true;
                _silenceTimer?.Change(_silencePadMilliseconds, _silencePadMilliseconds);
            }
            VisualizerAudioTrace.Log("loopback StartRecording succeeded; capture is live", force: true);
        }
        catch (Exception ex)
        {
            VisualizerAudioTrace.Log($"loopback StartRecording FAILED: {ex}", force: true);
            Debug.WriteLine($"WasapiLoopbackCaptureSource start failed: {ex}");
        }
    }

    /// <summary>
    /// Stops loopback capture and discards any buffered samples in the inner source.
    /// The NAudio recorder (and its underlying WASAPI <c>AudioClient</c>) is fully disposed and
    /// released so a subsequent <see cref="Start"/> builds a fresh recorder with a clean audio
    /// client — NAudio throws <c>0x88890002 "The audio client is already initialized"</c> if a
    /// stopped recorder is restarted (which silently froze the visualizer while audio kept playing).
    /// </summary>
    public void Stop()
    {
        WasapiRecorder? recorder;
        lock (_gate)
        {
            _started = false;
            _silenceTimer?.Change(global::System.Threading.Timeout.Infinite, global::System.Threading.Timeout.Infinite);
            recorder = _recorder;
            _recorder = null;
        }

        if (recorder != null)
        {
            recorder.DataAvailable -= OnDataAvailable;
            recorder.RecordingStopped -= OnRecordingStopped;
            ReleaseRecorder(recorder);
        }

        VisualizerAudioTrace.Log($"loopback stopped; packets={_packetCount} samples={_sampleCount} recentPeakF={_recentPeak.ToString("0.0000")}", force: true);
        _packetCount = 0;
        _sampleCount = 0;
        _recentPeak = 0;

        _inner.Stop();
    }

    /// <inheritdoc />
    public void Start()
    {
        // Synchronous interface member; the actual (async) recorder build and start are fire-and-forget.
        // The async path is guarded by _started so concurrent calls are safe.
        _ = StartAsync();
    }

    /// <inheritdoc />
    public void PushSamples(ReadOnlySpan<float> samples, int sampleRate, int channelCount)
        => _inner.PushSamples(samples, sampleRate, channelCount);

    private void OnDataAvailable(ReadOnlySpan<byte> buffer, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        try
        {
            var format = GetWaveFormat();
            if (format == null || buffer.IsEmpty)
            {
                VisualizerAudioTrace.Log($"DataAvailable skipped (format={format != null}, bytes={buffer.Length})");
                return;
            }

            Interlocked.Increment(ref _packetCount);

            // Convert the captured bytes to interleaved float PCM and push to the analyser. The per-batch
            // rate/channels/bit-depth come from the capture format (device mix format), never assumed.
            var samples = WasapiLoopbackPcmConverter.ConvertToFl32(buffer, format.SampleRate, format.Channels, format.Encoding, format.BitsPerSample);
            if (samples.Count > 0)
            {
                Interlocked.Add(ref _sampleCount, samples.Count);
                float peak = 0f;
                for (var i = 0; i < samples.Count; i++)
                {
                    var a = Math.Abs(samples.Samples[i]);
                    if (a > peak) peak = a;
                }

                if (peak > _recentPeak)
                {
                    _recentPeak = peak;
                }

                VisualizerAudioTrace.Log($"DataAvailable packet={_packetCount} bytes={buffer.Length} samples={samples.Count} peakF={peak.ToString("0.0000")} fmt={format.SampleRate}Hz/{format.Channels}ch/{format.Encoding}");

                _inner.PushSamples(samples.Samples.AsSpan(0, samples.Count), format.SampleRate, format.Channels);
            }
        }
        catch (Exception ex)
        {
            // A capture-packet fault must not tear down the capture thread or the player; the next
            // packet continues.
            VisualizerAudioTrace.Log($"DataAvailable fault: {ex}", force: true);
            System.Diagnostics.Debug.WriteLine($"WasapiLoopbackCaptureSource data fault: {ex}");
        }
    }

    private void OnRecordingStopped(object? s, StoppedEventArgs e)
    {
        // No-op: the source remains available; Stop() handles the actual teardown.
    }

    private void PadSilenceIfIdle()
    {
        // WASAPI loopback could fire no DataAvailable while our process renders nothing/silence
        // (NAudio delivers zeroed silent packets for silent buffers, but a long gap with no output
        // still yields no callbacks). Push a bounded silent frame so the visualizer never freezes.
        var format = GetWaveFormat();
        if (format == null)
        {
            return;
        }
        var silence = WasapiLoopbackPcmConverter.CreateSilence(format.SampleRate, format.Channels);
        if (silence.Count > 0)
        {
            _inner.PushSamples(silence.Samples.AsSpan(0, silence.Count), format.SampleRate, format.Channels);
        }
    }

    private WaveFormat? GetWaveFormat()
    {
        lock (_gate)
        {
            return _recorder?.WaveFormat;
        }
    }

    private static void ReleaseRecorder(WasapiRecorder recorder)
    {
        try
        {
            recorder.StopRecording();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WasapiLoopbackCaptureSource stop failed: {ex}");
        }

        try
        {
            recorder.Dispose();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WasapiLoopbackCaptureSource dispose failed: {ex}");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        lock (_gate)
        {
            _silenceTimer?.Dispose();
            _silenceTimer = null;
            _recorder = null;
        }
    }
}