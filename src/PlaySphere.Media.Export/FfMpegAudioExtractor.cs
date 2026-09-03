using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using PlaySphere.Application.Media;
using PlaySphere.Domain.Media;

namespace PlaySphere.Media.Export;

/// <summary>
/// <see cref="IAudioExtractor"/> backed by a bounded <c>ffmpeg.exe</c> subprocess (AI-02). Extracts
/// the audio of a local media file into a Whisper-compatible mono 16 kHz PCM WAV (PICKLE.md §38:
/// audio is preferred over full video for transcription). The input is untrusted (PICKLE.md §43), so
/// every run:
///   • passes arguments as a structured array (never shell-string concatenation),
///   • redirects and caps stderr so a hostile file cannot balloon memory,
///   • enforces <see cref="FfMpegAudioExtractOptions.Timeout"/> and honors cancellation,
///   • fails softly — a missing tool, missing input or a subprocess error returns an
///     <see cref="AudioExtractionResult.Fail"/> result instead of throwing.
/// </summary>
public sealed class FfMpegAudioExtractor(ILogger<FfMpegAudioExtractor> logger, FfMpegAudioExtractOptions? options = null) : IAudioExtractor
{
    private readonly FfMpegAudioExtractOptions _options = options ?? new FfMpegAudioExtractOptions();
    private readonly object _pathLock = new();
    private string? _resolvedExecutable;

    public async Task<AudioExtractionResult> ExtractAudioAsync(
        string inputPath,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        // Fail softly, never throw (untrusted caller input, PICKLE.md §43).
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            return AudioExtractionResult.Fail("No input file was specified.");
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            return AudioExtractionResult.Fail("No output file was specified.");
        }

        if (!File.Exists(inputPath))
        {
            return AudioExtractionResult.Fail($"Input file does not exist: {inputPath}");
        }

        if (string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase))
        {
            return AudioExtractionResult.Fail("The output path must differ from the input file.");
        }

        if (!string.Equals(Path.GetExtension(outputPath), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            return AudioExtractionResult.Fail("Audio extraction produces a .wav file.");
        }

        var executable = ResolveExecutable();
        if (executable is null)
        {
            return AudioExtractionResult.Fail("ffmpeg was not found; audio extraction is unavailable.");
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
        // Decode to mono 16 kHz signed 16-bit PCM WAV — the smallest Whisper-friendly upload and a
        // downmix that keeps speech intelligible without needing to know the source channel layout.
        psi.ArgumentList.Add("-y"); // overwrite the chosen output (the caller already asked)
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(inputPath);
        psi.ArgumentList.Add("-vn"); // no video — audio only, PICKLE.md §38
        psi.ArgumentList.Add("-ac");
        psi.ArgumentList.Add("1");
        psi.ArgumentList.Add("-ar");
        psi.ArgumentList.Add("16000");
        psi.ArgumentList.Add("-c:a");
        psi.ArgumentList.Add("pcm_s16le");
        psi.ArgumentList.Add(outputPath);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.Timeout);

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start())
            {
                return AudioExtractionResult.Fail("Failed to start ffmpeg.");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            logger.LogWarning(ex, "Could not start ffmpeg for audio extraction.");
            return AudioExtractionResult.Fail($"Could not start ffmpeg: {ex.Message}");
        }

        try
        {
            // Extraction is a heavy subprocess — run it below normal priority so it can never
            // starve ongoing playback (PICKLE.md §30).
            TryLowerPriority(process);

            // Drain both pipes asynchronously so a full stdout/stderr buffer never deadlocks the child.
            var stdoutTask = ReadToEndAsyncIgnoredAsync(process);
            var stderrTask = ReadCappedStderrAsync(process, timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                return AudioExtractionResult.Fail($"ffmpeg exited {process.ExitCode}: {LastErrorLine(stderr)}");
            }

            if (!File.Exists(outputPath))
            {
                return AudioExtractionResult.Fail("ffmpeg reported success but no audio file was produced.");
            }

            return AudioExtractionResult.Ok(outputPath);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Audio extraction timed out after {Timeout}.", _options.Timeout);
            CancelProcess(process);
            TryDeletePartialOutput(outputPath);
            return AudioExtractionResult.Fail($"Audio extraction timed out after {_options.Timeout.TotalMinutes:0} minutes.");
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
