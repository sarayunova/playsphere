namespace PlaySphere.Domain.Library;

/// <summary>
/// A folder the user has explicitly added to their library (PICKLE.md §22: never automatically
/// scan all drives — folders are only ever added one at a time by explicit user action).
/// </summary>
public sealed class LibraryFolder
{
    public int Id { get; set; }

    /// <summary>Absolute, canonicalized path to the folder.</summary>
    public required string Path { get; set; }

    /// <summary>UTC. Stored as <see cref="DateTime"/> rather than <see cref="DateTimeOffset"/> —
    /// the EF Core SQLite provider cannot translate ORDER BY over DateTimeOffset columns.</summary>
    public DateTime AddedAt { get; set; }
}
