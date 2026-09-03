namespace PlaySphere.Media.Export;

/// <summary>Limits for the bounded ffmpeg thumbnail-strip subprocess (untrusted input, PICKLE.md §43).</summary>
public sealed class ThumbnailStripOptions
{
    /// <summary>Path to ffmpeg.exe; when null the provider looks beside the app, else it fails softly.</summary>
    public string? ExecutablePath { get; set; }

    /// <summary>Maximum wall-clock time for a single strip generation.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>Hard cap on captured stderr bytes, so a chatty ffmpeg can never balloon memory.</summary>
    public int MaxErrorBytes { get; set; } = 64 * 1024;

    /// <summary>Upper bound on the grid cell count; a longer file still gets at most this many frames.</summary>
    public int MaxCells { get; set; } = 96;

    /// <summary>Cell width for the contact sheet. Bounded to 1–640 elsewhere.</summary>
    public int CellWidth { get; set; } = 160;

    /// <summary>Cell height for the contact sheet. Bounded to 1–640 elsewhere.</summary>
    public int CellHeight { get; set; } = 90;
}