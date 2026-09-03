using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using PlaySphere.Application.Media;
using PlaySphere.Domain.Media;

namespace PlaySphere.Media.Export;

/// <summary>
/// Bounded ffmpeg-backed timeline hover-thumbnail filmstrip (PICKLE.md §20 "hover thumbnail").
/// A media file is untrusted (PICKLE.md §43), so every run:
/// - uses a structured argument array, never a shell command string (AGENTS.md rule 4 / §43),
/// - samples frames from the whole duration via a single ffmpeg run producing one contact sheet
///   (a single file, so the UI crops a cell by index — no per-frame files, no seek spam on libVLC),
/// - is bounded by <see cref="ThumbnailStripOptions.Timeout"/> + capped stderr, and
/// - caches the finished sheet keyed on the file's identity inputs (path + mtime + length).
/// Failure always surfaces as <see cref="ThumbnailStripResult.Success"/> == false — never a throw.
/// </summary>
public sealed class FfMpegThumbnailProvider : IMediaThumbnailProvider
{
    private readonly ILogger<FfMpegThumbnailProvider> _logger;
    private readonly ThumbnailStripOptions _options;
    private readonly object _pathLock = new();
    private string? _resolvedExecutable;

    public FfMpegThumbnailProvider(ILogger<FfMpegThumbnailProvider> logger, ThumbnailStripOptions? options = null)
    {
        _logger = logger;
        _options = options ?? new ThumbnailStripOptions();
    }

    public async Task<ThumbnailStripResult> GenerateStripAsync(
        string filePath,
        string cacheDirectory,
        TimeSpan duration,
        int frameCount,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return ThumbnailStripResult.Fail("No media file path was given for thumbnail generation.");
        }

        if (!File.Exists(filePath))
        {
            return ThumbnailStripResult.Fail($"Input file does not exist: {filePath}");
        }

        if (duration <= TimeSpan.Zero)
        {
            return ThumbnailStripResult.Fail("Cannot sample thumbnails for media with an unknown duration (live or streamed).");
        }

        var cells = Math.Clamp(frameCount, 1, Math.Max(1, _options.MaxCells));
        var cellWidth = Math.Clamp(_options.CellWidth, 1, 640);
        var cellHeight = Math.Clamp(_options.CellHeight, 1, 640);

        var executable = ResolveExecutable();
        if (executable is null)
        {
            return ThumbnailStripResult.Fail("ffmpeg was not found; hover thumbnails are unavailable.");
        }

        var cacheKey = BuildCacheKey(filePath);
        var columns = (int)Math.Ceiling(Math.Sqrt(cells));
        var rows = (int)Math.Ceiling(cells / (double)columns);

        string? directory = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(cacheDirectory))
            {
                directory = Directory.CreateDirectory(cacheDirectory).FullName;
                var cached = Path.Combine(directory, cacheKey + ".jpg");
                if (File.Exists(cached))
                {
                    return ThumbnailStripResult.Ok(cached, columns, rows, cellWidth, cellHeight);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.LogWarning(ex, "Thumbnail cache directory unusable; generating without caching.");
            directory = null;
        }

        var outputPath = Path.Combine(directory ?? Path.GetTempPath(), cacheKey + ".jpg");
        var framesPerSecond = cells / duration.TotalSeconds;
        var filterGraph = $"fps={framesPerSecond:0.######},scale={cellWidth}:{cellHeight}:force_original_aspect_ratio=decrease,pad={cellWidth}:{cellHeight}:(ow-iw)/2:(oh-ih)/2,tile={columns}x{rows}";

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
        psi.ArgumentList.Add("-vf");
        psi.ArgumentList.Add(filterGraph);
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
                return ThumbnailStripResult.Fail("Failed to start ffmpeg.");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            _logger.LogWarning(ex, "Could not start ffmpeg for thumbnail generation.");
            return ThumbnailStripResult.Fail($"Could not start ffmpeg: {ex.Message}");
        }

        try
        {
            _ = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderr = await ReadCappedStderrAsync(process, timeoutCts.Token).ConfigureAwait(false);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                TryDeletePartialOutput(outputPath);
                return ThumbnailStripResult.Fail($"ffmpeg exited {process.ExitCode}: {LastErrorLine(stderr)}");
            }

            if (!File.Exists(outputPath))
            {
                return ThumbnailStripResult.Fail("ffmpeg reported success but no thumbnail sheet was produced.");
            }

            return ThumbnailStripResult.Ok(outputPath, columns, rows, cellWidth, cellHeight);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Thumbnail generation timed out after {Timeout}.", _options.Timeout);
            CancelProcess(process);
            TryDeletePartialOutput(outputPath);
            return ThumbnailStripResult.Fail($"Thumbnail generation timed out after {_options.Timeout.TotalSeconds:0} seconds.");
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