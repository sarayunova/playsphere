using Microsoft.Extensions.Logging.Abstractions;
using PlaySphere.Application.Media;
using PlaySphere.Domain.Media;
using PlaySphere.Worker;
using Xunit;

namespace PlaySphere.IntegrationTests;

/// <summary>
/// Deterministic queue tests using fake converter/exporter/probe implementations (no real ffmpeg).
/// The queue runs jobs on its own background worker, so tests wait on the internal idle hook instead
/// of sleeping. Mirrors PICKLE.md §30: progress, cancel, retry, serial execution.
/// </summary>
public sealed class ConversionQueueTests
{
    private static string CreateTempInput(string? content = null)
    {
        var path = Path.Combine(Path.GetTempPath(), $"playsphere-q-{Guid.NewGuid():N}.mp4");
        File.WriteAllBytes(path, content is null ? [1, 2, 3, 4] : System.Text.Encoding.UTF8.GetBytes(content));
        return path;
    }

    private static ConversionQueue CreateQueue(
        IMediaConverter? converter = null,
        IMediaClipExporter? exporter = null,
        IMediaProbe? probe = null)
    {
        var queue = new ConversionQueue(
            NullLogger<ConversionQueue>.Instance,
            converter,
            exporter,
            probe)
        {
            ProgressThrottle = TimeSpan.Zero,
        };
        return queue;
    }

    private sealed class FakeConverter : IMediaConverter
    {
        public ConversionResult Result { get; set; } = ConversionResult.Ok;
        public int FailuresRemaining { get; set; }
        public TaskCompletionSource? Gate { get; set; }
        public bool HonorCancellation { get; set; }
        public int Calls { get; private set; }

        public async Task<ConversionResult> ConvertAsync(
            string inputPath, string outputPath, ConversionPreset preset,
            CancellationToken cancellationToken = default, IProgress<TimeSpan>? progress = null)
        {
            Calls++;
            if (HonorCancellation)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            if (Gate is not null)
            {
                await Gate.Task.WaitAsync(cancellationToken);
            }

            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                return ConversionResult.Fail("transient failure");
            }

            return string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase)
                ? ConversionResult.Fail("same path")
                : Result;
        }
    }

    private sealed class FakeExporter : IMediaClipExporter
    {
        public ClipExportResult Result { get; set; } = ClipExportResult.Ok;
        public TimeSpan ProgressStep { get; set; } = TimeSpan.Zero;
        public TaskCompletionSource? Gate { get; set; }

        public async Task<ClipExportResult> ExportClipAsync(
            string inputPath, string outputPath, TimeSpan start, TimeSpan duration,
            CancellationToken cancellationToken = default, IProgress<TimeSpan>? progress = null)
        {
            if (ProgressStep > TimeSpan.Zero)
            {
                for (var t = TimeSpan.Zero; t < duration; t += ProgressStep)
                {
                    progress?.Report(t);
                }
            }

            if (Gate is not null)
            {
                await Gate.Task.WaitAsync(cancellationToken);
            }

            return Result;
        }
    }

    private sealed class FakeProbe : IMediaProbe
    {
        public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(120);

        public Task<MediaProbeResult?> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
            => Task.FromResult<MediaProbeResult?>(new MediaProbeResult(
                Duration, 1920, 1080, "1920x1080", "h264", 0, 1, 0, 0));
    }

    [Fact]
    public async Task EnqueueConvert_succeeds_and_marks_job_succeeded()
    {
        var input = CreateTempInput();
        var output = Path.Combine(Path.GetTempPath(), $"playsphere-{Guid.NewGuid():N}.mp4");
        try
        {
            using (var queue = CreateQueue(converter: new FakeConverter()))
            {
                Assert.True(queue.EnqueueConvert(input, output, ConversionPresets.All[0]));
                await queue.WaitForIdleAsync();

                var job = Assert.Single(queue.Jobs);
                Assert.Equal(ConversionJobStatus.Succeeded, job.Status);
                Assert.NotNull(job.Id);
            }
        }
        finally
        {
            File.Delete(input);
            File.Delete(output);
        }
    }

    [Fact]
    public void EnqueueConvert_rejects_missing_input_without_creating_a_job()
    {
        using var queue = CreateQueue(converter: new FakeConverter());
        var output = Path.Combine(Path.GetTempPath(), "missing.mp4");
        try
        {
            Assert.False(queue.EnqueueConvert(@"Z:\does\not\exist.mp4", output, ConversionPresets.All[0]));
            Assert.Empty(queue.Jobs);
        }
        finally
        {
            File.Delete(output);
        }
    }

    [Fact]
    public void EnqueueClip_rejects_invalid_bounds()
    {
        using var queue = CreateQueue(exporter: new FakeExporter());
        var input = CreateTempInput();
        try
        {
            Assert.False(queue.EnqueueClip(input, Path.Combine(Path.GetTempPath(), "c.mp4"), TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(5)));
            Assert.False(queue.EnqueueClip(input, Path.Combine(Path.GetTempPath(), "c.mp4"), TimeSpan.Zero, TimeSpan.Zero));
            Assert.False(queue.EnqueueClip(input, Path.Combine(Path.GetTempPath(), "c.txt"), TimeSpan.Zero, TimeSpan.FromSeconds(5)));
            Assert.Empty(queue.Jobs);
        }
        finally
        {
            File.Delete(input);
        }
    }

    [Fact]
    public async Task Jobs_run_serially_one_at_a_time()
    {
        var input1 = CreateTempInput();
        var input2 = CreateTempInput();
        var release = new TaskCompletionSource();
        var converter = new FakeConverter { Gate = release };
        try
        {
            using var queue = CreateQueue(converter: converter);

            Assert.True(queue.EnqueueConvert(input1, Path.Combine(Path.GetTempPath(), "a.mp4"), ConversionPresets.All[0]));
            Assert.True(queue.EnqueueConvert(input2, Path.Combine(Path.GetTempPath(), "b.mp4"), ConversionPresets.All[0]));

            // The first job must reach Running before we assert the second stays Queued.
            await WaitUntilAsync(() => queue.Jobs.Any(j => j.Status == ConversionJobStatus.Running));
            Assert.Single(queue.Jobs, j => j.Status == ConversionJobStatus.Running);
            Assert.Single(queue.Jobs, j => j.Status == ConversionJobStatus.Queued);

            release.SetResult();
            await queue.WaitForIdleAsync();
            Assert.All(queue.Jobs, j => Assert.Equal(ConversionJobStatus.Succeeded, j.Status));
        }
        finally
        {
            File.Delete(input1);
            File.Delete(input2);
        }
    }

    [Fact]
    public async Task Cancel_while_running_marks_the_job_cancelled()
    {
        var input = CreateTempInput();
        var converter = new FakeConverter { HonorCancellation = true };
        try
        {
            using var queue = CreateQueue(converter: converter);
            var output = Path.Combine(Path.GetTempPath(), "cancel.mp4");
            Assert.True(queue.EnqueueConvert(input, output, ConversionPresets.All[0]));

            await WaitUntilAsync(() => queue.Jobs.Any(j => j.Status == ConversionJobStatus.Running));
            queue.Cancel(queue.Jobs.First().Id);
            await queue.WaitForIdleAsync();

            Assert.Equal(ConversionJobStatus.Cancelled, Assert.Single(queue.Jobs).Status);
            File.Delete(output);
        }
        finally
        {
            File.Delete(input);
        }
    }

    [Fact]
    public async Task Retry_reruns_a_failed_job()
    {
        var input = CreateTempInput();
        var converter = new FakeConverter { FailuresRemaining = 1 };
        try
        {
            using var queue = CreateQueue(converter: converter);
            var output = Path.Combine(Path.GetTempPath(), "retry.mp4");
            Assert.True(queue.EnqueueConvert(input, output, ConversionPresets.All[0]));
            await queue.WaitForIdleAsync();

            var failed = Assert.Single(queue.Jobs);
            Assert.Equal(ConversionJobStatus.Failed, failed.Status);
            Assert.Equal(1, converter.Calls);

            queue.Retry(failed.Id);
            await queue.WaitForIdleAsync();

            var retried = Assert.Single(queue.Jobs);
            Assert.Equal(ConversionJobStatus.Succeeded, retried.Status);
            Assert.Equal(1, retried.RetryCount);
            Assert.Equal(2, converter.Calls);
            File.Delete(output);
        }
        finally
        {
            File.Delete(input);
        }
    }

    [Fact]
    public async Task Clip_job_reports_progress_against_the_segment_duration()
    {
        var input = CreateTempInput();
        var exporter = new FakeExporter { ProgressStep = TimeSpan.FromSeconds(1) };
        try
        {
            using var queue = CreateQueue(exporter: exporter);
            var output = Path.Combine(Path.GetTempPath(), "clip.mp4");
            Assert.True(queue.EnqueueClip(input, output, TimeSpan.Zero, TimeSpan.FromSeconds(10)));
            await queue.WaitForIdleAsync();

            var job = Assert.Single(queue.Jobs);
            Assert.Equal(ConversionJobStatus.Succeeded, job.Status);
            Assert.Equal(TimeSpan.FromSeconds(10), job.TotalDuration);
            Assert.True(job.OutTime >= TimeSpan.FromSeconds(9), $"OutTime was {job.OutTime}");
            Assert.True(job.ProgressFraction >= 0.9, $"ProgressFraction was {job.ProgressFraction}");
            File.Delete(output);
        }
        finally
        {
            File.Delete(input);
        }
    }

    [Fact]
    public async Task Convert_job_uses_probed_duration_for_the_progress_fraction()
    {
        var input = CreateTempInput();
        try
        {
            using var queue = CreateQueue(converter: new FakeConverter(), probe: new FakeProbe { Duration = TimeSpan.FromSeconds(120) });
            var output = Path.Combine(Path.GetTempPath(), "probe.mp4");
            Assert.True(queue.EnqueueConvert(input, output, ConversionPresets.All[0]));
            await queue.WaitForIdleAsync();

            var job = Assert.Single(queue.Jobs);
            Assert.Equal(TimeSpan.FromSeconds(120), job.TotalDuration);
            File.Delete(output);
        }
        finally
        {
            File.Delete(input);
        }
    }

    [Fact]
    public async Task JobsChanged_fires_on_enqueue_and_on_completion()
    {
        var input = CreateTempInput();
        var events = 0;
        try
        {
            using var queue = CreateQueue(converter: new FakeConverter());
            queue.JobsChanged += (_, _) => events++;
            Assert.True(queue.EnqueueConvert(input, Path.Combine(Path.GetTempPath(), "ev.mp4"), ConversionPresets.All[0]));
            Assert.True(events >= 1);

            await queue.WaitForIdleAsync();
            Assert.True(events >= 2, $"expected enqueue + completion events, got {events}");
        }
        finally
        {
            File.Delete(input);
        }
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("Condition was not satisfied in time.");
    }
}