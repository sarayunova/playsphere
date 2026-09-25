using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NAudio.Wave;
using PlaySphere.Application.Media;
using PlaySphere.Domain.Media;
using PlaySphere.Media.Export;

namespace PlaySphere.Audio;

/// <summary>
/// The live stereo-balance secondary audio path (ADR-0011 Stage 2,
/// docs/design/stereo-balance-av-sync.md). Spawns a bounded ffmpeg subprocess that decodes the
/// currently-loaded file's audio from a given seek position through a <c>pan</c> filter (the
/// requested <see cref="StereoBalance"/>) and streams raw float32 PCM to stdout, which is pumped
/// into a NAudio <see cref="WasapiOut"/> device — entirely additive to, and independent of,
/// libVLC's own audio/video pipeline (never rebuilds or reroutes it, per the crash history
/// recorded in AGENTS.md).
///
/// Sync strategy: the engine's video position is authoritative (design note item 1). This session
/// never seeks the source file for itself except when told to (an explicit start, an engine seek,
/// a balance change, or a periodic drift check per <see cref="StereoBalanceSyncPolicy"/>) — every
/// correction is a full stop-reseek-restart of the ffmpeg segment (design note items 2-4), which
/// produces a brief audio gap rather than attempting sample-accurate resampling.
///
/// Deliberately NOT wired into <c>PlayerViewModel</c>/the UI yet — that is a separate follow-up
/// slice once this session class itself is reviewed.
/// </summary>
public sealed class StereoBalanceAudioSession : IStereoBalanceAudioSession, IAsyncDisposable
{
    private readonly ILogger<StereoBalanceAudioSession> _logger;
    private readonly string? _executablePath;
    private readonly object _gate = new();

    private CancellationTokenSource? _sessionCts;
    private Task? _resyncTask;
    private Task? _pumpTask;
    private Process? _process;
    private IWavePlayer? _output;
    private string? _filePath;
    private StereoBalance _balance = StereoBalance.Center;
    private DateTime _segmentStartUtc;
    private TimeSpan _segmentStartPosition;
    private volatile bool _active;

    public StereoBalanceAudioSession(ILogger<StereoBalanceAudioSession> logger, StereoBalanceAudioOptions? options = null)
    {
        _logger = logger;
        _executablePath = ResolveExecutable(options?.ExecutablePath);
    }

    /// <summary>True while the secondary audio path is running.</summary>
    public bool IsActive => _active;

    /// <summary>The balance most recently applied (whether or not the session is currently active).</summary>
    public StereoBalance CurrentBalance => _balance;

    /// <summary>
    /// Starts the secondary audio path for <paramref name="filePath"/> at
    /// <paramref name="getEnginePosition"/>'s current value, applying <paramref name="balance"/>.
    /// Best-effort: returns false (never throws) if ffmpeg is unavailable or fails to start; the
    /// caller should treat that as "balance audio unavailable, continue with normal playback."
    /// </summary>
    public async Task<bool> StartAsync(
        string filePath,
        StereoBalance balance,
        Func<TimeSpan> getEnginePosition,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        if (_executablePath is null)
        {
            _logger.LogWarning("ffmpeg was not found; stereo balance audio is unavailable.");
            return false;
        }

        await StopAsync().ConfigureAwait(false);

        lock (_gate)
        {
            _filePath = filePath;
            _balance = balance.Clamped();
        }

        var position = SafePosition(getEnginePosition);
        var started = await StartSegmentAsync(position, cancellationToken).ConfigureAwait(false);
        if (!started)
        {
            lock (_gate)
            {
                _filePath = null;
            }
            return false;
        }

        _sessionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _resyncTask = RunResyncLoopAsync(getEnginePosition, _sessionCts.Token);
        _active = true;
        return true;
    }

    /// <summary>
    /// Applies a new balance value, restarting the segment at the engine's current position (a
    /// balance change requires a new <c>pan</c> filter, which ffmpeg cannot hot-swap mid-stream).
    /// </summary>
    public Task<bool> UpdateBalanceAsync(StereoBalance balance, Func<TimeSpan> getEnginePosition, CancellationToken cancellationToken = default)
    {
        if (!_active)
        {
            return Task.FromResult(false);
        }

        lock (_gate)
        {
            _balance = balance.Clamped();
        }

        return RestartSegmentAsync(SafePosition(getEnginePosition), cancellationToken);
    }

    /// <summary>
    /// Tells the session the engine transport jumped to a new position (a user seek, pause/resume
    /// handoff, etc. — design note item 4); restarts the segment there.
    /// </summary>
    public Task<bool> NotifyEngineSeekedAsync(TimeSpan newPosition, CancellationToken cancellationToken = default)
    {
        if (!_active)
        {
            return Task.FromResult(false);
        }

        return RestartSegmentAsync(newPosition, cancellationToken);
    }

    /// <summary>Stops the secondary audio path entirely (design note item 5). Safe to call when inactive.</summary>
    public async Task StopAsync()
    {
        _active = false;

        var cts = _sessionCts;
        _sessionCts = null;
        cts?.Cancel();

        var resyncTask = _resyncTask;
        _resyncTask = null;
        if (resyncTask is not null)
        {
            try
            {
                await resyncTask.ConfigureAwait(false);
            }
            catch
            {
                // Best-effort — the loop's own try/catch already swallows expected cancellation.
            }
        }

        cts?.Dispose();

        await StopSegmentAsync().ConfigureAwait(false);

        lock (_gate)
        {
            _filePath = null;
        }
    }

    private async Task<bool> RestartSegmentAsync(TimeSpan position, CancellationToken cancellationToken)
    {
        await StopSegmentAsync().ConfigureAwait(false);
        var started = await StartSegmentAsync(position, cancellationToken).ConfigureAwait(false);
        if (!started)
        {
            _active = false;
        }
        return started;
    }

    private async Task<bool> StartSegmentAsync(TimeSpan position, CancellationToken cancellationToken)
    {
        string? path;
        StereoBalance balance;
        lock (_gate)
        {
            path = _filePath;
            balance = _balance;
        }

        if (path is null || _executablePath is null)
        {
            return false;
        }

        IReadOnlyList<string> args;
        try
        {
            args = StereoBalanceAudioArgs.BuildStreamArguments(path, position, balance);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Could not build stereo balance ffmpeg arguments.");
            return false;
        }

        var psi = new ProcessStartInfo
        {
            FileName = _executablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start())
            {
                process.Dispose();
                return false;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            _logger.LogWarning(ex, "Could not start ffmpeg for stereo balance audio.");
            process.Dispose();
            return false;
        }

        TryLowerPriority(process);

        var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(StereoBalanceAudioArgs.OutputSampleRateHz, 2);
        var buffer = new BufferedWaveProvider(waveFormat)
        {
            DiscardOnBufferOverflow = true,
        };

        IWavePlayer? output = null;
        try
        {
            // NAudio 3's WasapiOut is obsolete in favor of the WasapiPlayerBuilder/WasapiPlayer
            // pair (mirrors the WasapiRecorderBuilder pattern ADR-0009 already uses for capture).
            output = new WasapiPlayerBuilder().WithSharedMode().Build();
            output.Init(buffer);
            output.Play();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not start stereo balance audio output device.");
            output?.Dispose();
            KillProcessBestEffort(process);
            process.Dispose();
            return false;
        }

        lock (_gate)
        {
            _process = process;
            _output = output;
            _segmentStartUtc = DateTime.UtcNow;
            _segmentStartPosition = position;
        }

        _pumpTask = PumpStdoutAsync(process, buffer, cancellationToken);
        _ = DrainStderrIgnoredAsync(process);
        return true;
    }

    private async Task StopSegmentAsync()
    {
        Process? process;
        IWavePlayer? output;
        lock (_gate)
        {
            process = _process;
            output = _output;
            _process = null;
            _output = null;
        }

        try
        {
            output?.Stop();
        }
        catch
        {
            // Best-effort teardown.
        }
        output?.Dispose();

        if (process is not null)
        {
            KillProcessBestEffort(process);
            process.Dispose();
        }

        var pumpTask = _pumpTask;
        _pumpTask = null;
        if (pumpTask is not null)
        {
            try
            {
                await pumpTask.ConfigureAwait(false);
            }
            catch
            {
                // Best-effort — the pump's own try/catch already swallows expected failures.
            }
        }
    }

    private async Task PumpStdoutAsync(Process process, BufferedWaveProvider buffer, CancellationToken cancellationToken)
    {
        var stream = process.StandardOutput.BaseStream;
        var chunk = new byte[16 * 1024];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                buffer.AddSamples(chunk, 0, read);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on stop/restart.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Stereo balance audio pump ended.");
        }
    }

    private async Task RunResyncLoopAsync(Func<TimeSpan> getEnginePosition, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(StereoBalanceSyncPolicy.CheckInterval, cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                TimeSpan estimatedAudioPosition;
                lock (_gate)
                {
                    if (_output is null)
                    {
                        continue;
                    }

                    // Approximate: wall-clock elapsed since the current segment started, not a
                    // sample-accurate readback (NAudio's BufferedWaveProvider does not expose
                    // exact playback position). The design note accepts this — sub-10ms accuracy
                    // is explicitly out of scope.
                    var elapsed = DateTime.UtcNow - _segmentStartUtc;
                    estimatedAudioPosition = _segmentStartPosition + elapsed;
                }

                var enginePosition = SafePosition(getEnginePosition);
                if (StereoBalanceSyncPolicy.ShouldResync(enginePosition, estimatedAudioPosition))
                {
                    _logger.LogDebug("Stereo balance audio drifted; resyncing to {Position}.", enginePosition);
                    await RestartSegmentAsync(enginePosition, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on StopAsync.
        }
    }

    private static TimeSpan SafePosition(Func<TimeSpan> getter)
    {
        try
        {
            var position = getter();
            return position < TimeSpan.Zero ? TimeSpan.Zero : position;
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    private static async Task DrainStderrIgnoredAsync(Process process)
    {
        try
        {
            await process.StandardError.ReadToEndAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort drain to avoid a full-pipe deadlock; content is not needed.
        }
    }

    private static void KillProcessBestEffort(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill();
            }
        }
        catch
        {
            // Best-effort — never take the caller down cleaning up.
        }
    }

    private static void TryLowerPriority(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
        }
        catch
        {
            // Best-effort — never take the caller down tuning the child.
        }
    }

    private static string? ResolveExecutable(string? explicitPath)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return File.Exists(explicitPath) ? explicitPath : null;
        }

        var baseDir = AppContext.BaseDirectory;
        foreach (var candidate in new[]
                 {
                     Path.Combine(baseDir, "ffmpeg.exe"),
                     Path.Combine(baseDir, "ffmpeg", "ffmpeg.exe"),
                 })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
    }
}
