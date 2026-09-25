using PlaySphere.Domain.Media;
using PlaySphere.Media.Export;
using Xunit;

namespace PlaySphere.IntegrationTests;

/// <summary>
/// Pure tests for the stereo-balance ffmpeg argument builder (ADR-0011 Stage 2). No process is
/// spawned — these assert the constructed argument sequence only, per
/// docs/design/stereo-balance-av-sync.md's "-ss before -i" seek pattern.
/// </summary>
public sealed class StereoBalanceAudioArgsTests
{
    [Fact]
    public void BuildStreamArguments_places_seek_before_input_matching_the_clip_export_pattern()
    {
        var args = StereoBalanceAudioArgs.BuildStreamArguments("C:\\media\\movie.mp4", TimeSpan.FromSeconds(12.5), StereoBalance.Center);

        var ssIndex = IndexOf(args, "-ss");
        var iIndex = IndexOf(args, "-i");
        Assert.True(ssIndex >= 0 && iIndex >= 0 && ssIndex < iIndex);
        Assert.Equal("12.5", args[ssIndex + 1]);
        Assert.Equal("C:\\media\\movie.mp4", args[iIndex + 1]);
    }

    [Fact]
    public void BuildStreamArguments_streams_raw_float32_pcm_to_stdout()
    {
        var args = StereoBalanceAudioArgs.BuildStreamArguments("in.mp4", TimeSpan.Zero, StereoBalance.Center);

        Assert.Equal("pipe:1", args[^1]);
        var fIndex = IndexOf(args, "-f");
        Assert.Equal("f32le", args[fIndex + 1]);
        var arIndex = IndexOf(args, "-ar");
        Assert.Equal(StereoBalanceAudioArgs.OutputSampleRateHz.ToString(), args[arIndex + 1]);
        var acIndex = IndexOf(args, "-ac");
        Assert.Equal("2", args[acIndex + 1]);
    }

    [Fact]
    public void BuildStreamArguments_negative_seek_is_clamped_to_zero()
    {
        var args = StereoBalanceAudioArgs.BuildStreamArguments("in.mp4", TimeSpan.FromSeconds(-5), StereoBalance.Center);
        var ssIndex = IndexOf(args, "-ss");
        Assert.Equal("0", args[ssIndex + 1]);
    }

    [Fact]
    public void BuildStreamArguments_throws_on_empty_input_path()
    {
        Assert.Throws<ArgumentException>(() =>
            StereoBalanceAudioArgs.BuildStreamArguments(string.Empty, TimeSpan.Zero, StereoBalance.Center));
    }

    [Fact]
    public void BuildStreamArguments_never_uses_shell_string_concatenation()
    {
        // Structured argument array only (AGENTS.md rule 4 / §43) — no single argument should
        // itself contain a shell metacharacter sequence combining flag+value.
        var args = StereoBalanceAudioArgs.BuildStreamArguments("in.mp4", TimeSpan.Zero, StereoBalance.Center);
        Assert.DoesNotContain(args, a => a.Contains(' ') && a.StartsWith('-'));
    }

    [Theory]
    [InlineData(0f, 1f, "pan=stereo|c0=0.000*c0|c1=1.000*c1")]
    [InlineData(1f, 1f, "pan=stereo|c0=1.000*c0|c1=1.000*c1")]
    [InlineData(0.5f, 1f, "pan=stereo|c0=0.500*c0|c1=1.000*c1")]
    [InlineData(1f, 0.5f, "pan=stereo|c0=1.000*c0|c1=0.500*c1")]
    public void BuildPanFilter_formats_the_expected_ffmpeg_expression(float left, float right, string expected)
    {
        Assert.Equal(expected, StereoBalanceAudioArgs.BuildPanFilter(left, right));
    }

    [Fact]
    public void BuildStreamArguments_uses_the_gain_computed_from_balance()
    {
        var args = StereoBalanceAudioArgs.BuildStreamArguments("in.mp4", TimeSpan.Zero, new StereoBalance(50));
        var afIndex = IndexOf(args, "-af");
        Assert.Equal("pan=stereo|c0=0.500*c0|c1=1.000*c1", args[afIndex + 1]);
    }

    private static int IndexOf(IReadOnlyList<string> args, string value)
    {
        for (var i = 0; i < args.Count; i++)
        {
            if (args[i] == value)
            {
                return i;
            }
        }

        return -1;
    }
}
