using System.Diagnostics;

namespace PlaySphere.IntegrationTests;

/// <summary>
/// Locates the checked-in FFmpeg/FFprobe binaries (<c>tools\ffmpeg</c>) and generates small,
/// legally-safe synthetic media fixtures for probe/scan tests. Never ships copyrighted media.
/// </summary>
internal static class MediaTestTools
{
    /// <summary>Walks up from the test output directory to find a file under the repo.</summary>
    public static string? FindExecutable(params string[] relativeSegments)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var depth = 0; depth < 12 && dir is not null; depth++, dir = dir.Parent)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(relativeSegments).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    public static string? FfProbe => FindExecutable("tools", "ffmpeg", "ffprobe.exe");

    public static string? Ffmpeg => FindExecutable("tools", "ffmpeg", "ffmpeg.exe");

    /// <summary>
    /// Generates a ~2s synthetic 320x180 h264/AAC MP4 at <paramref name="outputPath"/> using the
    /// bundled ffmpeg. Structured argument array only (PICKLE.md §29 — never shell strings).
    /// Redirected pipes are drained asynchronously to avoid a subprocess deadlock.
    /// </summary>
    public static async Task<string> GenerateSampleVideoAsync(string outputPath)
    {
        var ffmpeg = Ffmpeg ?? throw new InvalidOperationException("ffmpeg.exe not found in tools/ffmpeg.");
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("lavfi");
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add("testsrc=size=320x180:rate=5:duration=2");
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("lavfi");
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add("sine=frequency=440:duration=2");
        psi.ArgumentList.Add("-c:v"); psi.ArgumentList.Add("libx264");
        psi.ArgumentList.Add("-pix_fmt"); psi.ArgumentList.Add("yuv420p");
        psi.ArgumentList.Add("-c:a"); psi.ArgumentList.Add("aac");
        psi.ArgumentList.Add("-shortest");
        psi.ArgumentList.Add(outputPath);

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        await stdoutTask.ConfigureAwait(false);
        await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to generate sample media; ffmpeg exited {process.ExitCode}.");
        }
        return outputPath;
    }

    /// <summary>
    /// Generates a ~2s synthetic audio-only WAV (440 Hz sine, PCM 16-bit) using the bundled ffmpeg.
    /// Structured argument array only (§29). Redirected pipes are drained asynchronously.
    /// </summary>
    public static async Task<string> GenerateSampleAudioAsync(string outputPath)
    {
        var ffmpeg = Ffmpeg ?? throw new InvalidOperationException("ffmpeg.exe not found in tools/ffmpeg.");
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("lavfi");
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add("sine=frequency=440:duration=2");
        psi.ArgumentList.Add("-c:a"); psi.ArgumentList.Add("pcm_s16le");
        psi.ArgumentList.Add(outputPath);

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        await stdoutTask.ConfigureAwait(false);
        await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to generate sample audio; ffmpeg exited {process.ExitCode}.");
        }
        return outputPath;
    }

    /// <summary>
    /// Generates a ~2s synthetic MP4 with video but NO audio track (testsrc only, <c>-an</c>).
    /// Structured argument array only (§29). Redirected pipes are drained asynchronously.
    /// </summary>
    public static async Task<string> GenerateSampleVideoNoAudioAsync(string outputPath)
    {
        var ffmpeg = Ffmpeg ?? throw new InvalidOperationException("ffmpeg.exe not found in tools/ffmpeg.");
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-y");
        psi.ArgumentList.Add("-f"); psi.ArgumentList.Add("lavfi");
        psi.ArgumentList.Add("-i"); psi.ArgumentList.Add("testsrc=size=320x180:rate=5:duration=2");
        psi.ArgumentList.Add("-c:v"); psi.ArgumentList.Add("libx264");
        psi.ArgumentList.Add("-pix_fmt"); psi.ArgumentList.Add("yuv420p");
        psi.ArgumentList.Add("-an");
        psi.ArgumentList.Add(outputPath);

        using var process = Process.Start(psi)!;
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        await stdoutTask.ConfigureAwait(false);
        await stderrTask.ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to generate sample media; ffmpeg exited {process.ExitCode}.");
        }
        return outputPath;
    }
}
