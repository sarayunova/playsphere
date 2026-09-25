namespace PlaySphere.Domain.Media;

/// <summary>
/// A continuous left/right stereo balance value (ADR-0011). Distinct from
/// <see cref="AudioChannelMode"/>'s coarse full-pan cases: this expresses a partial attenuation of
/// one channel rather than muting it outright. -100 is full left (right channel silent), 0 is
/// centered (both channels at full gain), +100 is full right (left channel silent).
/// </summary>
public sealed record StereoBalance(int Value)
{
    /// <summary>Centered — both channels at full gain, the default/no-op balance.</summary>
    public static StereoBalance Center { get; } = new(0);

    /// <summary>Minimum balance value (full left).</summary>
    public const int MinValue = -100;

    /// <summary>Maximum balance value (full right).</summary>
    public const int MaxValue = 100;

    /// <summary>Clamps an arbitrary integer into the valid <see cref="MinValue"/>..<see cref="MaxValue"/> range.</summary>
    public static int Clamp(int value) => Math.Clamp(value, MinValue, MaxValue);

    /// <summary>Returns an equivalent <see cref="StereoBalance"/> clamped into the valid range.</summary>
    public StereoBalance Clamped() => Value is >= MinValue and <= MaxValue ? this : new StereoBalance(Clamp(Value));

    /// <summary>True when this balance is centered (no attenuation on either channel).</summary>
    public bool IsCentered => Value == 0;
}

/// <summary>
/// Pure left/right gain-coefficient math for a <see cref="StereoBalance"/> value (ADR-0011). No
/// engine, filter-graph or process dependency — this is the shared calculation the eventual
/// FFmpeg <c>pan</c>/<c>volume</c> filter arguments and any UI preview are both built from, kept
/// separately unit-testable from the (not-yet-implemented) audio routing itself.
/// </summary>
public static class StereoBalanceGain
{
    /// <summary>
    /// Computes the (Left, Right) linear gain coefficients (0.0-1.0) for a balance value. A
    /// positive balance attenuates the left channel proportionally while the right channel stays
    /// at full gain (and vice versa for negative) — a "balance" taper (the active channel is
    /// never boosted), not a constant-power pan law. An out-of-range input is clamped first.
    /// </summary>
    public static (float Left, float Right) Compute(int balance)
    {
        var clamped = StereoBalance.Clamp(balance);

        if (clamped == 0)
        {
            return (1f, 1f);
        }

        if (clamped > 0)
        {
            // Shifted right: left channel attenuates toward 0 as balance approaches +100.
            var left = 1f - (clamped / 100f);
            return (left, 1f);
        }

        // Shifted left: right channel attenuates toward 0 as balance approaches -100.
        var right = 1f + (clamped / 100f);
        return (1f, right);
    }
}
