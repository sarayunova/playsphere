using System.Globalization;
using PlaySphere.Domain.Media;

namespace PlaySphere.Media.Export;

/// <summary>
/// Pure FFmpeg argument builder for the stereo-balance secondary audio path
/// (<c>docs/design/stereo-balance-av-sync.md</c>, ADR-0011 Stage 2). Builds the structured
/// <c>ArgumentList</c>-style argument sequence (never a shell-string, per AGENTS.md rule 4 / §43)
/// that decodes an input file's audio starting at a given seek position, applies the requested
/// <see cref="StereoBalance"/> as a <c>pan</c> filter, and streams raw interleaved 32-bit float
/// PCM to stdout for a caller (the not-yet-implemented NAudio output stage) to consume. No
/// process is started here — this is argument construction only, kept separately testable from
/// live process spawning.
/// </summary>
public static class StereoBalanceAudioArgs
{
    /// <summary>Output sample rate the stream is resampled to (matches common device defaults).</summary>
    public const int OutputSampleRateHz = 48000;

    /// <summary>
    /// Builds the ffmpeg arguments to decode <paramref name="inputPath"/>'s audio from
    /// <paramref name="seekFrom"/> onward, apply <paramref name="balance"/> as a stereo pan
    /// filter, and stream raw interleaved float32 PCM to stdout. A centered balance still emits
    /// the pan filter (with unity gains) for a single, uniform code path rather than branching
    /// output format handling on whether balance is active.
    /// </summary>
    public static IReadOnlyList<string> BuildStreamArguments(string inputPath, TimeSpan seekFrom, StereoBalance balance)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        var clampedSeek = seekFrom < TimeSpan.Zero ? TimeSpan.Zero : seekFrom;
        var (left, right) = StereoBalanceGain.Compute(balance.Value);

        return
        [
            "-hide_banner",
            "-loglevel", "error",
            "-ss", clampedSeek.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            "-i", inputPath,
            "-vn",
            "-af", BuildPanFilter(left, right),
            "-ar", OutputSampleRateHz.ToString(CultureInfo.InvariantCulture),
            "-ac", "2",
            "-f", "f32le",
            "pipe:1",
        ];
    }

    /// <summary>
    /// Builds the ffmpeg <c>pan</c> filter expression for the given linear left/right gain
    /// coefficients (e.g. <c>pan=stereo|c0=0.500*c0|c1=1.000*c1</c>). Public and pure so the exact
    /// filter syntax is independently testable without spawning ffmpeg.
    /// </summary>
    public static string BuildPanFilter(float left, float right)
    {
        var l = left.ToString("0.000", CultureInfo.InvariantCulture);
        var r = right.ToString("0.000", CultureInfo.InvariantCulture);
        return $"pan=stereo|c0={l}*c0|c1={r}*c1";
    }
}
