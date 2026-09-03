using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PlaySphere.Application.Media;
using PlaySphere.Domain.Media;

namespace PlaySphere.Media.Probe;

/// <summary>
/// <see cref="IMediaProbe"/> backed by a bounded <c>ffprobe.exe</c> subprocess (PICKLE.md §23).
/// A media file is untrusted (PICKLE.md §43), so every probe:
///   • passes arguments as a structured array (never shell-string concatenation),
///   • redirects and caps stdout so a hostile file cannot balloon memory,
///   • enforces <see cref="FfProbeOptions.Timeout"/> and honors cancellation,
///   • fails softly — a missing tool, unreadable file or parse error returns
///     <c>null</c> instead of propagating, so library scans degrade gracefully.
/// </summary>
public sealed class FfProbeMediaProbe(ILogger<FfProbeMediaProbe> logger, FfProbeOptions? options = null) : IMediaProbe
{
    private readonly FfProbeOptions _options = options ?? new FfProbeOptions();
    private readonly object _pathLock = new();
    private string? _resolvedExecutable;

    public async Task<MediaProbeResult?> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filePath);

        var executable = ResolveExecutable();
        if (executable is null)
        {
            logger.LogWarning("No ffprobe executable found; skipping technical probe for '{Path}'.", filePath);
            return null;
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
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-show_format");
        psi.ArgumentList.Add("-show_streams");
        psi.ArgumentList.Add("-print_format");
        psi.ArgumentList.Add("json");
        psi.ArgumentList.Add(filePath);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_options.Timeout);

        using var process = new Process { StartInfo = psi };
        try
        {
            if (!process.Start())
            {
                logger.LogWarning("Failed to start ffprobe for '{Path}'.", filePath);
                return null;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            logger.LogWarning(ex, "Could not start ffprobe for '{Path}'.", filePath);
            return null;
        }

        try
        {
            var stdoutTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

            var output = await stdoutTask.ConfigureAwait(false);
            await stderrTask.ConfigureAwait(false);

            if (process.ExitCode != 0)
            {
                logger.LogDebug("ffprobe exited with {ExitCode} for '{Path}'.", process.ExitCode, filePath);
                return null;
            }

            return Parse(output, filePath);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Timed out probing '{Path}' after {Timeout}.", filePath, _options.Timeout);
            try { process.Kill(); } catch { /* best effort */ }
            return null;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(); } catch { /* best effort */ }
            throw;
        }
        finally
        {
            if (!process.HasExited)
            {
                try { process.Kill(); } catch { /* best effort */ }
            }
        }
    }

    private MediaProbeResult? Parse(string json, string filePath)
    {
        try
        {
            if (json.Length > _options.MaxOutputBytes)
            {
                logger.LogWarning("Probe output for '{Path}' exceeded the byte cap; skipping.", filePath);
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var format = root.TryGetProperty("format", out var fmt) ? fmt : default;

            double durationSeconds = 0;
            if (format.TryGetProperty("duration", out var dur) &&
                double.TryParse(dur.GetString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var d))
            {
                durationSeconds = d;
            }

            long? bitrateKbps = null;
            if (format.TryGetProperty("bit_rate", out var br) &&
                long.TryParse(br.GetString(), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var bits))
            {
                bitrateKbps = bits / 1000;
            }

            int? width = null;
            int? height = null;
            string? codec = null;
            double? frameRate = null;
            var audioCount = 0;
            var subtitleCount = 0;

            if (root.TryGetProperty("streams", out var streams) && streams.ValueKind == JsonValueKind.Array)
            {
                foreach (var stream in streams.EnumerateArray())
                {
                    var type = stream.TryGetProperty("codec_type", out var ct) ? ct.GetString() : null;
                    switch (type)
                    {
                        case "video" when width is null && height is null:
                            if (stream.TryGetProperty("width", out var w)) width = w.GetInt32();
                            if (stream.TryGetProperty("height", out var h)) height = h.GetInt32();
                            if (stream.TryGetProperty("codec_name", out var cn) && codec is null) codec = cn.GetString();

                            if (stream.TryGetProperty("avg_frame_rate", out var fr) &&
                                ParseRational(fr.GetString(), out var fps) && frameRate is null)
                            {
                                frameRate = fps;
                            }
                            break;

                        case "audio":
                            audioCount++;
                            break;

                        case "subtitle":
                            subtitleCount++;
                            break;
                    }
                }
            }

            string? resolution = null;
            if (width is int w2 && height is int h2)
            {
                resolution = $"{w2}x{h2}";
            }

            return new MediaProbeResult(
                TimeSpan.FromSeconds(durationSeconds),
                width,
                height,
                resolution,
                codec,
                frameRate,
                bitrateKbps,
                audioCount,
                subtitleCount);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Could not parse ffprobe output for '{Path}'.", filePath);
            return null;
        }
    }

    private static bool ParseRational(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text) || text is "0/0" or "N/A")
        {
            return false;
        }

        var slash = text.IndexOf('/');
        if (slash > 0 &&
            double.TryParse(text[..slash], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var num) &&
            double.TryParse(text[(slash + 1)..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var den) &&
            den != 0 && num > 0)
        {
            value = num / den;
            return value > 0;
        }

        return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) && value > 0;
    }

    /// <summary>Resolves the ffprobe executable, preferring an explicitly configured path.</summary>
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
                         Path.Combine(baseDir, "ffprobe.exe"),
                         Path.Combine(baseDir, "ffmpeg", "ffprobe.exe"),
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
