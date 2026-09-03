using Microsoft.Extensions.Logging;
using PlaySphere.Application.Media;
using PlaySphere.Domain.Media;

namespace PlaySphere.Worker;

/// <summary>
/// Serial background queue for conversion/clip-export jobs (PICKLE.md §30). A single worker drains
/// jobs one at a time so a heavy queue can never compete against itself or starve ongoing playback
/// (subprocesses also run below normal priority, applied at the converter/exporter layer).
///
/// Each job carries progress (mapped from ffmpeg's absolute output timestamp against the probed
/// input duration — or the clip segment duration), and supports cancel, retry and open-output-folder.
/// All mutations happen under a lock; <see cref="Jobs"/> returns snapshot copies and
/// <see cref="JobsChanged"/> fires on every add/update/finish so the UI can refresh cheaply.
/// </summary>
public sealed class ConversionQueue : IConversionQueue
{
    private readonly IMediaConverter? _converter;
    private readonly IMediaClipExporter? _exporter;
    private readonly IMediaProbe? _probe;
    private readonly ILogger<ConversionQueue> _logger;
    private readonly object _gate = new();
    private readonly List<ConversionJob> _jobs = [];
    private readonly Dictionary<string, CancellationTokenSource> _ctss = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _disposeCts = new();
    private Task? _worker;
    private bool _disposed;

    /// <summary>How often progress updates are surfaced to the UI (default 250 ms; lowered in tests).</summary>
    public TimeSpan ProgressThrottle { get; set; } = TimeSpan.FromMilliseconds(250);

    public ConversionQueue(
        ILogger<ConversionQueue> logger,
        IMediaConverter? converter = null,
        IMediaClipExporter? exporter = null,
        IMediaProbe? probe = null)
    {
        _logger = logger;
        _converter = converter;
        _exporter = exporter;
        _probe = probe;
    }

    public event EventHandler? JobsChanged;

    public IReadOnlyList<ConversionJob> Jobs
    {
        get
        {
            lock (_gate)
            {
                return _jobs.AsReadOnly();
            }
        }
    }

    public bool EnqueueConvert(string inputPath, string outputPath, ConversionPreset preset)
    {
        // Mirror the converter's own soft-fail validation so an invalid job never occupies a slot.
        if (string.IsNullOrWhiteSpace(inputPath) || string.IsNullOrWhiteSpace(outputPath) || preset is null ||
            !File.Exists(inputPath) ||
            string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_converter is null)
        {
            return false;
        }

        NewJob(
            ConversionJobKind.Convert,
            inputPath,
            outputPath,
            preset.DisplayName,
            convertPreset: preset);
        return true;
    }

    public bool EnqueueClip(string inputPath, string outputPath, TimeSpan start, TimeSpan duration)
    {
        var extension = string.IsNullOrWhiteSpace(outputPath) ? string.Empty : Path.GetExtension(outputPath);
        if (string.IsNullOrWhiteSpace(inputPath) || string.IsNullOrWhiteSpace(outputPath) ||
            !File.Exists(inputPath) ||
            string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase) ||
            !AllowedClipExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase) ||
            start < TimeSpan.Zero || duration <= TimeSpan.Zero)
        {
            return false;
        }

        if (_exporter is null)
        {
            return false;
        }

        NewJob(
            ConversionJobKind.ClipExport,
            inputPath,
            outputPath,
            "Clip export",
            start: start,
            clipDuration: duration);
        return true;
    }

    public void Cancel(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return;
        }

        lock (_gate)
        {
            var index = _jobs.FindIndex(j => j.Id == jobId);
            if (index < 0)
            {
                return;
            }

            var job = _jobs[index];
            if (job.Status == ConversionJobStatus.Queued)
            {
                _jobs[index] = job with { Status = ConversionJobStatus.Cancelled };
                RaiseJobsChanged();
            }
            else if (job.Status == ConversionJobStatus.Running)
            {
                _ctss.TryGetValue(jobId, out var cts);
                cts?.Cancel();
            }
        }
    }

    public void Retry(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            return;
        }

        lock (_gate)
        {
            var index = _jobs.FindIndex(j => j.Id == jobId);
            if (index < 0 || _jobs[index].Status != ConversionJobStatus.Failed)
            {
                return;
            }

            var current = _jobs[index];
            _jobs[index] = current with
            {
                Status = ConversionJobStatus.Queued,
                ErrorMessage = null,
                OutTime = TimeSpan.Zero,
                RetryCount = current.RetryCount + 1,
            };
        }

        PumpWorker();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            _disposeCts.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        lock (_gate)
        {
            foreach (var cts in _ctss.Values)
            {
                try
                {
                    cts.Cancel();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        try
        {
            _worker?.GetAwaiter().GetResult();
        }
        catch
        {
            // Worker exits via OCE shutdown; never throw from Dispose.
        }

        _disposeCts.Dispose();
        lock (_gate)
        {
            foreach (var cts in _ctss.Values)
            {
                cts.Dispose();
            }

            _ctss.Clear();
        }
    }

    private static readonly string[] AllowedClipExtensions = [".mp4", ".mkv", ".mov", ".webm", ".avi"];

    private void NewJob(
        ConversionJobKind kind,
        string inputPath,
        string outputPath,
        string label,
        ConversionPreset? convertPreset = null,
        TimeSpan start = default,
        TimeSpan clipDuration = default)
    {
        lock (_gate)
        {
            _jobs.Add(new ConversionJob(
                Guid.NewGuid().ToString("N"),
                kind,
                inputPath,
                outputPath,
                label,
                ConversionJobStatus.Queued,
                ErrorMessage: null,
                OutTime: TimeSpan.Zero,
                TotalDuration: TimeSpan.Zero,
                RetryCount: 0,
                CreatedAt: DateTimeOffset.UtcNow,
                convertPreset,
                start,
                clipDuration));
            RaiseJobsChanged();
        }

        PumpWorker();
    }

    private void PumpWorker()
    {
        lock (_gate)
        {
            if (_worker is null || _worker.IsCompleted)
            {
                _worker = DrainAsync(_disposeCts.Token);
            }
        }
    }

    private async Task DrainAsync(CancellationToken shutdown)
    {
        while (!shutdown.IsCancellationRequested)
        {
            ConversionJob? next = null;
            lock (_gate)
            {
                var index = _jobs.FindIndex(j => j.Status == ConversionJobStatus.Queued);
                if (index >= 0)
                {
                    next = _jobs[index];
                    _jobs[index] = next with { Status = ConversionJobStatus.Running };
                    RaiseJobsChanged();
                    if (!_ctss.ContainsKey(next.Id))
                    {
                        _ctss[next.Id] = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
                    }
                }
            }

            if (next is null)
            {
                return;
            }

            await RunJobAsync(next, shutdown).ConfigureAwait(false);
        }
    }

    private async Task RunJobAsync(ConversionJob job, CancellationToken shutdown)
    {
        var totalDuration = await ProbeDurationAsync(job, shutdown).ConfigureAwait(false);
        var lastRaised = DateTimeOffset.UtcNow;

        var progress = new Progress<TimeSpan>(outTime =>
        {
            var now = DateTimeOffset.UtcNow;
            lock (_gate)
            {
                var index = _jobs.FindIndex(j => j.Id == job.Id);
                if (index < 0)
                {
                    return;
                }

                var current = _jobs[index];
                _jobs[index] = current with { OutTime = outTime > current.OutTime ? outTime : current.OutTime };

                if (now - lastRaised >= ProgressThrottle)
                {
                    lastRaised = now;
                    RaiseJobsChanged();
                }
            }
        });

        try
        {
            CancellationTokenSource? jobCts;
            lock (_gate)
            {
                _ctss.TryGetValue(job.Id, out jobCts);
            }

            var ct = jobCts?.Token ?? shutdown;

            ConversionJob finished;
            if (job.Kind == ConversionJobKind.Convert)
            {
                var result = await _converter!.ConvertAsync(job.InputPath, job.OutputPath, job.ConvertPreset!, ct, progress).ConfigureAwait(false);
                finished = result.Success
                    ? job with { Status = ConversionJobStatus.Succeeded }
                    : job with { Status = ConversionJobStatus.Failed, ErrorMessage = result.ErrorMessage };
            }
            else
            {
                var result = await _exporter!.ExportClipAsync(job.InputPath, job.OutputPath, job.Start, job.ClipDuration, ct, progress).ConfigureAwait(false);
                finished = result.Success
                    ? job with { Status = ConversionJobStatus.Succeeded }
                    : job with { Status = ConversionJobStatus.Failed, ErrorMessage = result.ErrorMessage };
            }

            PublishTerminal(job.Id, finished, totalDuration);
        }
        catch (OperationCanceledException)
        {
            PublishTerminal(job.Id, job with { Status = ConversionJobStatus.Cancelled }, totalDuration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background job {JobId} ({Kind}) failed unexpectedly.", job.Id, job.Kind);
            PublishTerminal(job.Id, job with { Status = ConversionJobStatus.Failed, ErrorMessage = ex.Message }, totalDuration);
        }
        finally
        {
            CancellationTokenSource? cts;
            lock (_gate)
            {
                if (_ctss.Remove(job.Id, out var c))
                {
                    cts = c;
                }
                else
                {
                    cts = null;
                }
            }

            if (cts != null)
            {
                try
                {
                    cts.Dispose();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }
    }

    private void PublishTerminal(string jobId, ConversionJob finished, TimeSpan totalDuration)
    {
        lock (_gate)
        {
            var index = _jobs.FindIndex(j => j.Id == jobId);
            if (index < 0)
            {
                return;
            }

            // Preserve any newer progress OutTime the throttle may have skipped, and attach the
            // probed duration so the progress fraction stays readable for finished jobs.
            var live = _jobs[index];
            finished = finished with
            {
                OutTime = live.OutTime > finished.OutTime ? live.OutTime : finished.OutTime,
                TotalDuration = totalDuration > TimeSpan.Zero ? totalDuration : (finished.TotalDuration > TimeSpan.Zero ? finished.TotalDuration : live.TotalDuration),
            };
            _jobs[index] = finished;
            RaiseJobsChanged();
        }
    }

    private void RaiseJobsChanged()
    {
        JobsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Waits for every queued/running job to reach a terminal state (test hook). Returns
    /// immediately when the queue is idle; times out after <paramref name="timeout"/>.
    /// </summary>
    internal async Task WaitForIdleAsync(TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        while (DateTimeOffset.UtcNow < deadline)
        {
            lock (_gate)
            {
                if (!_jobs.Any(j => j.IsActive))
                {
                    return;
                }
            }

            await Task.Delay(10).ConfigureAwait(false);
        }

        throw new TimeoutException("Conversion queue did not become idle in time.");
    }

    private async Task<TimeSpan> ProbeDurationAsync(ConversionJob job, CancellationToken shutdown)
    {
        if (job.Kind == ConversionJobKind.ClipExport)
        {
            // For a clip the segment length is the exact output total — a genuine 0..1 progress.
            return job.ClipDuration > TimeSpan.Zero ? job.ClipDuration : TimeSpan.Zero;
        }

        // For conversions the output total ~= the input duration (probed once per job, best-effort).
        if (_probe is null || !File.Exists(job.InputPath))
        {
            return TimeSpan.Zero;
        }

        try
        {
            var result = await _probe.ProbeAsync(job.InputPath, shutdown).ConfigureAwait(false);
            return result?.Duration ?? TimeSpan.Zero;
        }
        catch (OperationCanceledException)
        {
            return TimeSpan.Zero;
        }
        catch (Exception)
        {
            return TimeSpan.Zero;
        }
    }
}