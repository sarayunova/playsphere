using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using PlaySphere.Application.Media;
using PlaySphere.Domain.Media;

namespace PlaySphere.Media.Export;

/// <summary>
/// Bounded ffmpeg-backed timeline waveform (PICKLE.md §20 "waveform", PS-705). The whole scrubbable
/// track gets a quiet spike-level visual aid. A media file is untrusted (PICKLE.md §43), so every run:
/// - uses a structured argument array, never a shell command string (AGENTS.md rule 4 / §43),
/// - asks ffmpeg's <c>showwavespic</c> sink for a single PNG of the FULL duration's peak levels
///   (one file, no per-window analysis shipping gigabytes of PCM around),
/// - is bounded by <see cref="WaveformOptions.Timeout"/> + capped stderr, and
/// - caches the finished strip keyed on the file's identity inputs (path + mtime + length).
/// Failure always surfaces as <see cref="WaveformResult.Success"/> == false — never a throw.
/// </summary>
public sealed class FfMpegWaveformProvider : IWaveformProvider
{
    private readonly ILogger<FfMpegWaveformProvider> _logger;
    private readonly WaveformOptions _options;
    private readonly object _pathLock = new();
    private string? _resolvedExecutable;

    public FfMpegWaveformProvider(ILogger<FfMpegWaveformProvider> logger, WaveformOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new WaveformOptions();
    }

    public async Task<WaveformResult> GenerateAsync(
        string filePath,
        string cacheDirectory,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return WaveformResult.Fail("No media file path was given for waveform generation.");
        }

        if (!File.Exists(filePath))
        {
            return WaveformResult.Fail($"Input file does not exist: {filePath}");
        }

        var width = Math.Clamp(_options.Width, 64, 2048);
        var height = Math.Clamp(_options.Height, 16, 256);

        var executable = ResolveExecutable();
        if (executable is null)
        {
            return WaveformResult.Fail("ffmpeg was not found; the timeline waveform is unavailable.");
        }

        var cacheKey = BuildCacheKey(filePath);
        string? directory = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(cacheDirectory))
            {
                directory = Directory.CreateDirectory(cacheDirectory).FullName;
                var cached = Path.Combine(directory, cacheKey + ".png");
                if (File.Exists(cached))
                {
                    return WaveformResult.Ok(cached, width, height);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning(ex, "Waveform cache directory unusable; generating without caching.");
            directory = null;
        }

        var outputPath = Path.Combine(directory ?? Path.GetTempPath(), cacheKey + ".png");
        var filterGraph = $"showwavespic=s={width}x{height}:colors=white,format=rgba[wf]";

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
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-hide_banner");
        psi.ArgumentList.Add("-loglevel");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-i");
        psi.ArgumentList.Add(filePath);
        psi.ArgumentList.Add("-filter_complex");
        psi.ArgumentList.Add(filterGraph);
        psi.ArgumentList.Add("-map");
        psi.ArgumentList.Add("[wf]");
        psi.ArgumentList.Add("-frames:v");
        psi.ArgumentList.Add("1");
        psi.ArgumentList.Add(outputPath);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.Timeout);

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start())
            {
                return WaveformResult.Fail("Failed to start ffmpeg.");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            _logger.LogWarning(ex, "Could not start ffmpeg for waveform generation.");
            return WaveformResult.Fail($"Could not start ffmpeg: {ex.Message}");
        }

        try
        {
            _ = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderr = await ReadCappedStderrAsync(process, timeoutCts.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                TryDeletePartialOutput(outputPath);
                return WaveformResult.Fail($"ffmpeg exited {process.ExitCode}: {LastErrorLine(stderr)}");
            }

            if (!File.Exists(outputPath))
            {
                return WaveformResult.Fail("ffmpeg reported success but no waveform strip was produced.");
            }

            return WaveformResult.Ok(outputPath, width, height);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Waveform generation timed out after {Timeout}.", _options.Timeout);
            CancelProcess(process);
            TryDeletePartialOutput(outputPath);
            return WaveformResult.Fail($"Waveform generation timed out after {_options.Timeout.TotalSeconds:0} seconds.");
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

    /// <summary>Identity key: our own (untrusted-path-safe) hash of path + mtime + length (AGENTS' file-identity inputs).</summary>
    private static string BuildCacheKey(string filePath)
    {
        var info = new FileInfo(filePath);
        var stamp = $"{filePath.ToLowerInvariant()}|{info.LastWriteTimeUtc.Ticks}|{info.Length}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(stamp));
        return Convert.ToHexString(hash.AsSpan(0, 16)).ToLowerInvariant();
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