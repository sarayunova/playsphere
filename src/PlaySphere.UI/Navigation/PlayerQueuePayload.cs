namespace PlaySphere.UI.Navigation;

/// <summary>
/// Navigation payload that asks <see cref="Pages.PlayerPage"/> to load an ordered collection of
/// media files as a play queue (PS/Phase 5, PICKLE.md §25). <see cref="FilePaths"/> is the play
/// order; <see cref="StartFilePath"/> is the item to begin on (defaults to the first when null).
/// <see cref="Position"/> optionally asks the player to seek to a position (seconds-equivalent
/// time span) right after the start item loads — used by transcript/timestamp navigation (AI-05)
/// — and is best-effort: an unseekable or still-loading player simply ignores it.
/// </summary>
public sealed class PlayerQueuePayload
{
    public PlayerQueuePayload(IReadOnlyList<string> filePaths, string? startFilePath = null, TimeSpan? position = null)
    {
        FilePaths = filePaths ?? throw new ArgumentNullException(nameof(filePaths));
        StartFilePath = startFilePath;
        Position = position;
    }

    public IReadOnlyList<string> FilePaths { get; }

    public string? StartFilePath { get; }

    public TimeSpan? Position { get; }
}
