using PlaySphere.Domain.Media;
using Xunit;

namespace PlaySphere.UnitTests;

/// <summary>
/// Pure tests for the stereo balance value type and its gain-coefficient math (ADR-0011, Stage 1).
/// No engine/audio-routing code exists yet — these tests cover only the math that Stage 2's
/// FFmpeg/NAudio wiring will consume.
/// </summary>
public sealed class StereoBalanceTests
{
    [Fact]
    public void Center_is_the_default_and_is_centered()
    {
        Assert.Equal(0, StereoBalance.Center.Value);
        Assert.True(StereoBalance.Center.IsCentered);
    }

    [Theory]
    [InlineData(-200, -100)]
    [InlineData(-101, -100)]
    [InlineData(-100, -100)]
    [InlineData(0, 0)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    [InlineData(500, 100)]
    public void Clamp_bounds_to_min_max(int input, int expected)
    {
        Assert.Equal(expected, StereoBalance.Clamp(input));
    }

    [Fact]
    public void Clamped_returns_equivalent_instance_when_already_in_range()
    {
        var balance = new StereoBalance(42);
        Assert.Equal(42, balance.Clamped().Value);
    }

    [Fact]
    public void Clamped_clamps_an_out_of_range_instance()
    {
        var balance = new StereoBalance(9000);
        Assert.Equal(100, balance.Clamped().Value);
    }

    [Fact]
    public void IsCentered_false_for_nonzero_value()
    {
        Assert.False(new StereoBalance(1).IsCentered);
        Assert.False(new StereoBalance(-1).IsCentered);
    }

    [Fact]
    public void Compute_at_center_returns_full_gain_both_channels()
    {
        var (left, right) = StereoBalanceGain.Compute(0);
        Assert.Equal(1f, left);
        Assert.Equal(1f, right);
    }

    [Fact]
    public void Compute_full_right_silences_left_keeps_right_full()
    {
        var (left, right) = StereoBalanceGain.Compute(100);
        Assert.Equal(0f, left, 3);
        Assert.Equal(1f, right);
    }

    [Fact]
    public void Compute_full_left_silences_right_keeps_left_full()
    {
        var (left, right) = StereoBalanceGain.Compute(-100);
        Assert.Equal(1f, left);
        Assert.Equal(0f, right, 3);
    }

    [Theory]
    [InlineData(50, 0.5f, 1f)]
    [InlineData(25, 0.75f, 1f)]
    [InlineData(-50, 1f, 0.5f)]
    [InlineData(-25, 1f, 0.75f)]
    public void Compute_partial_balance_attenuates_only_the_opposite_channel(int balance, float expectedLeft, float expectedRight)
    {
        var (left, right) = StereoBalanceGain.Compute(balance);
        Assert.Equal(expectedLeft, left, 3);
        Assert.Equal(expectedRight, right, 3);
    }

    [Fact]
    public void Compute_clamps_out_of_range_input_before_computing()
    {
        var (left, right) = StereoBalanceGain.Compute(9000);
        Assert.Equal(0f, left, 3);
        Assert.Equal(1f, right);
    }

    [Fact]
    public void Compute_never_boosts_the_active_channel_above_full_gain()
    {
        for (var balance = StereoBalance.MinValue; balance <= StereoBalance.MaxValue; balance++)
        {
            var (left, right) = StereoBalanceGain.Compute(balance);
            Assert.InRange(left, 0f, 1f);
            Assert.InRange(right, 0f, 1f);
        }
    }
}
