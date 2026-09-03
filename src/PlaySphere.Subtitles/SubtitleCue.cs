namespace PlaySphere.Subtitles;

/// <summary>
/// A single subtitle cue: a time range and the text displayed during it.
/// </summary>
public sealed record SubtitleCue(int Index, TimeSpan Start, TimeSpan End, string Text)
{
    /// <summary>True when <see cref="End"/> is after <see cref="Start"/> and both are non-negative.</summary>
    public bool IsValid => Start >= TimeSpan.Zero && End > Start;
}
