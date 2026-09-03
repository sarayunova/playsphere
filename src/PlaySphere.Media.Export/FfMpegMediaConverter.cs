using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using PlaySphere.Application.Media;
using PlaySphere.Domain.Media;

namespace PlaySphere.Media.Export;

/// <summary>
/// <see cref="IMediaConverter"/> backed by a bounded <c>ffmpeg.exe</c> subprocess (PICKLE.md §29).
/// A media file is untrusted (PICKLE.md §43), so every conversion:
///   • passes arguments as a structured array (never shell-string concatenation),
///   • takes its codec/mapping instructions from a pure <see cref="ConversionPreset"/>,
///   • redirects and caps stderr so a hostile file cannot balloon memory,
///   • enforces <see cref="FfMpegConvertOptions.Timeout"/> and honors cancellation,
///   • fails softly — a missing tool, missing input, mismatched output extension or a subprocess
///     error returns a <see cref="ConversionResult.Fail"/> result instead of throwing.
/// </summary>
public sealed class FfMpegMediaConverter(ILogger<FfMpegMediaConverter> logger, FfMpegConvertOptions? options = null) : IMediaConverter
{
    private readonly FfMpegConvertOptions _options = options ?? new FfMpegConvertOptions();
    private readonly object _pathLock = new();
    private string? _resolvedExecutable;

    public async Task<ConversionResult> ConvertAsync(
        string inputPath,
        string outputPath,
        ConversionPreset preset,
        CancellationToken cancellationToken = default,
        IProgress<TimeSpan>? progress = null)
    {
        // Fail softly, never throw (untrusted caller input, PICKLE.md §43).
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return ConversionResult.Fail("No input file was specified.");
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return ConversionResult.Fail("No output file was specified.");
        }

        if (preset is null)
        {
            return ConversionResult.Fail("No conversion preset was chosen.");
        }

        if (!File.Exists(inputPath))
        {
            return ConversionResult.Fail($"Input file does not exist: {inputPath}");
        }

        if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            return ConversionResult.Fail("The output path must differ from the input file.");
        }

        var outputExtension = Path.GetExtension(outputPath).ToLowerInvariant();
        if (!string.Equals(outputExtension, preset.TargetExtension, StringComparison.OrdinalIgnoreCase))
        {
            return ConversionResult.Fail($"Output extension '{outputExtension}' does not match the '{preset.DisplayName}' preset.");
        }

        var executable = ResolveExecutable();
        if (executable is null)
        {
            return ConversionResult.Fail("ffmpeg was not found; conversion is unavailable.");
        }

        var psi = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        // Structured argument array, never a shell command string (AGENTS.md rule 4 / §43).
        psi.ArgumentList.Add("-y"); // overwrite the chosen output (the picker already asked)
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel");
        psi.ArgumentList.Add("error");
        if (progress is not null)
        {
            // Machine-readable key=value progress on stdout (out_time_us / out_time_ms),
            // consumed by ParseProgressAsync below. Never shell-string concatenation.
            psi.ArgumentList.Add("-progress");
            psi.ArgumentList.Add("pipe:1");
        }

        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(inputPath);
        foreach (var arg in preset.FfmpegArgs)
        {
            psi.ArgumentList.Add(arg);
        }

        psi.ArgumentList.Add(outputPath);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.Timeout);

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start())
            {
                return ConversionResult.Fail("Failed to start ffmpeg.");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            logger.LogWarning(ex, "Could not start ffmpeg for conversion.");
            return ConversionResult.Fail($"Could not start ffmpeg: {ex.Message}");
        }

        try
        {
            // Conversion is a heavy subprocess — run it below normal priority so a queue
            // of these can never starve ongoing playback (PICKLE.md §30).
            TryLowerPriority(process);

            // Drain both pipes asynchronously so a full stdout/stderr buffer never deadlocks the child.
            var stdout = progress is not null
                ? ParseProgressAsync(process, progress, timeoutCts.Token)
                : ReadToEndAsyncIgnoredAsync(process);
            var stderrTask = ReadCappedStderrAsync(process, timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            await Task.WhenAll(stdout, stderrTask).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                return ConversionResult.Fail($"ffmpeg exited {process.ExitCode}: {LastErrorLine(stderr)}");
            }

            if (!File.Exists(outputPath))
            {
                return ConversionResult.Fail("ffmpeg reported success but no output file was produced.");
            }

            return ConversionResult.Ok;
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Conversion timed out after {Timeout}.", _options.Timeout);
            CancelProcess(process);
            TryDeletePartialOutput(outputPath);
            return ConversionResult.Fail($"Conversion timed out after {_options.Timeout.TotalMinutes:0} minutes.");
        }
        catch (OperationCanceledException)
        {
            CancelProcess(process);
            TryDeletePartialOutput(outputPath);
            throw;
        }
        finally
        {
            if (!process.HasExited)
            {
                CancelProcess(process);
            }
        }
    }

    private async Task<string> ReadCappedStderrAsync(Process process, CancellationToken cancellationToken)
    {
        const int chunk = 4096;
        var buffer = new char[chunk];
        var capped = new StringBuilder(_options.MaxErrorBytes);
        var remaining = _options.MaxErrorBytes;
        try
        {
            while (remaining > 0)
            {
                var read = await process.StandardError.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (read <= 0)
                {
                    break;
                }

                var take = Math.Min(read, remaining);
                capped.Append(buffer, 0, take);
                remaining -= take;
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout/cancel is surfaced by the caller; a partial capture is fine.
        }

        return capped.ToString();
    }

    private static string LastErrorLine(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return "no error details from ffmpeg";
        }

        var lines = stderr.Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].TrimEnd('\r').Trim();
            if (line.Length > 0)
            {
                var marker = line.IndexOf("]: ", StringComparison.OrdinalIgnoreCase);
                return marker >= 0 ? line[(marker + 3)..] : line;
            }
        }

        return stderr.Trim();
    }

    private static void CancelProcess(Process process)
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
            // Best effort — never take the caller down cleaning up.
        }
    }

    private static void TryDeletePartialOutput(string outputPath)
    {
        try
        {
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    private static async Task ParseProgressAsync(Process process, IProgress<TimeSpan> progress, CancellationToken cancellationToken)
    {
        // ffmpeg -progress pipe:1 emits key=value lines; out_time_us (or out_time_ms on
        // older builds) is the absolute output timestamp consumed so far. Monotonic only.
        var last = TimeSpan.Zero;
        try
        {
            while (true)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                var eq = line.IndexOf('=');
                if (eq <= 0 || eq >= line.Length - 1)
                {
                    continue;
                }

                TimeSpan time;
                if (line.AsSpan(0, eq).SequenceEqual("out_time_us") && long.TryParse(line.AsSpan(eq + 1), out var us))
                {
                    // 1 us == 100 ns ticks.
                    time = TimeSpan.FromTicks(Math.Max(0, us) * 10);
                }
                else if (line.AsSpan(0, eq).SequenceEqual("out_time_ms") && long.TryParse(line.AsSpan(eq + 1), out var ms))
                {
                    time = TimeSpan.FromMilliseconds(Math.Max(0, ms));
                }
                else
                {
                    continue;
                }

                if (time > last)
                {
                    last = time;
                    progress.Report(time);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timeout/cancel is surfaced by the caller; partial progress is fine.
        }
    }

    private static async Task ReadToEndAsyncIgnoredAsync(Process process)
    {
        await process.StandardOutput.ReadToEndAsync(CancellationToken.None).ConfigureAwait(false);
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
            // Best effort — never take the caller down tuning the child.
        }
    }

    /// <summary>Resolves the ffmpeg executable, preferring an explicitly configured path.</summary>
    private string? ResolveExecutable()
    {
        if (!string.IsNullOrWhiteSpace(_options.ExecutablePath))
        {
            return File.Exists(_options.ExecutablePath) ? _options.ExecutablePath : null;
        }

        lock (_pathLock)
        {
            if (_resolvedExecutable is not null)
            {
                return _resolvedExecutable;
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
                    _resolvedExecutable = candidate;
                    return candidate;
                }
            }

            return null;
        }
    }
}