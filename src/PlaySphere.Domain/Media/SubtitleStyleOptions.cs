namespace PlaySphere.Domain.Media;

/// <summary>
/// Subtitle outline thickness (PICKLE.md §17 "styling"). Maps to libVLC's freetype
/// <c>freetype-outline-thickness</c> (0 = off, 1 = thin, 2 = normal).
/// </summary>
public enum SubtitleOutlineStyle
{
    Off = 0,
    Thin = 1,
    Normal = 2,
}

/// <summary>
/// Subtitle rendering style (PICKLE.md §17 "styling"). Applied via the engine's per-media
/// freetype options — changes take effect on the next <c>LoadAsync</c>, not instantaneously
/// mid-playback. Styling only applies to plain-text subtitle formats (SRT/WebVTT); rich-text
/// (ASS) and bitmap (DVD/PGS) subtitles render their own baked-in styling.
/// </summary>
/// <param name="TextScalePercent">Relative text size (25–500, default 100).</param>
/// <param name="FontFamily">Font family name, or null for the system default.</param>
/// <param name="TextColorArgb">Text color as 0xRRGGBB, or null for the default (white).</param>
/// <param name="OutlineColorArgb">Outline color as 0xRRGGBB, or null for the default (black).</param>
/// <param name="Outline">Outline thickness on/off/thin/normal.</param>
/// <param name="VerticalMarginPx">Vertical subtitle position as the bottom margin in pixels
/// (0 = bottom edge; higher = higher on screen).</param>
public sealed record SubtitleStyleOptions(
    int TextScalePercent = 100,
    string? FontFamily = null,
    int? TextColorArgb = null,
    int? OutlineColorArgb = null,
    SubtitleOutlineStyle Outline = SubtitleOutlineStyle.Off,
    int VerticalMarginPx = 0)
{
    public static SubtitleStyleOptions Default { get; } = new();
}
