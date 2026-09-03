namespace PlaySphere.Domain.Media;

/// <summary>Kind of background job a <see cref="ConversionJob"/> performs (PICKLE.md §30).</summary>
public enum ConversionJobKind
{
    /// <summary>Full transcode/remux/audio conversion via <c>IMediaConverter</c>.</summary>
    Convert,

    /// <summary>Stream-copied segment trim via <c>IMediaClipExporter</c>.</summary>
    ClipExport,
}

/// <summary>Lifecycle state of a background conversion job (PICKLE.md §30).</summary>
public enum ConversionJobStatus
{
    /// <summary>Awaiting a worker slot.</summary>
    Queued,

    /// <summary>Actively converting/clipping.</summary>
    Running,

    /// <summary>Completed with a usable output file.</summary>
    Succeeded,

    /// <summary>Failed on a user-correctable error; retry is allowed.</summary>
    Failed,

    /// <summary>Cancelled by the user before completion.</summary>
    Cancelled,
}

/// <summary>
/// A single background conversion/clip-export job (PICKLE.md §30). Immutable value type — a queue
/// publishes snapshots via <c>with</c> so consumers never observe partially-updated jobs.
/// </summary>
public sealed record ConversionJob(
    string Id,
    ConversionJobKind Kind,
    string InputPath,
    string OutputPath,
    string Label,
    ConversionJobStatus Status,
    string? ErrorMessage,
    TimeSpan OutTime,
    TimeSpan TotalDuration,
    int RetryCount,
    DateTimeOffset CreatedAt,
    ConversionPreset? ConvertPreset = null,
    TimeSpan Start = default,
    TimeSpan ClipDuration = default)
{
    /// <summary>
    /// 0..1 progress estimate when <c>TotalDuration</c> is known, else 0. Clamps out-of-range
    /// output timestamps (ffmpeg can emit a few milliseconds past the segment).
    /// </summary>
    public double ProgressFraction =>
        TotalDuration <= TimeSpan.Zero
            ? 0
            : Math.Clamp(OutTime.TotalSeconds / TotalDuration.TotalSeconds, 0, 1);

    /// <summary>Whether the user can still cancel this job from the queue UI.</summary>
    public bool IsActive => Status is ConversionJobStatus.Queued or ConversionJobStatus.Running;

    /// <summary>Whether the output file exists and the job finished successfully.</summary>
    public bool CanOpenOutputFolder => Status is ConversionJobStatus.Succeeded;

    /// <summary>Whether the failed job may be re-queued (retry).</summary>
    public bool CanRetry => Status is ConversionJobStatus.Failed;
}