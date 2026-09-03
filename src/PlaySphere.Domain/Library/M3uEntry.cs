namespace PlaySphere.Domain.Library;

/// <summary>
/// One media entry parsed from an M3U/M3U8 playlist (PICKLE.md §25, §43 untrusted-input).
/// <see cref="Path"/> is the resolved absolute path (a relative entry is resolved against the
/// playlist's directory); <see cref="Title"/> and <see cref="DurationSeconds"/>, when present,
/// come from an <c>#EXTINF</c> directive preceding the entry.
/// </summary>
public sealed record M3uEntry(string Path, string? Title, long? DurationSeconds);
