using System.Security.Cryptography;

namespace PlaySphere.Library;

/// <summary>
/// Computes a media file identity fingerprint from file length, modified timestamp and a
/// sampled cryptographic hash — never the path alone, so a renamed or moved file is still
/// recognized (PICKLE.md §24). Only small chunks are read regardless of file size: hashing an
/// entire multi-gigabyte video on every scan would make scanning impractically slow. A full-file
/// hash remains available separately (PICKLE.md §24 "optional full hash for verification") but
/// is not what this default fingerprint uses.
/// </summary>
public static class MediaFileFingerprinter
{
    private const int SampleChunkBytes = 64 * 1024;

    /// <summary>Computes the fingerprint for the file at <paramref name="filePath"/>.</summary>
    /// <exception cref="IOException">The file could not be opened or read.</exception>
    public static string Compute(string filePath)
    {
        var info = new FileInfo(filePath);
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        var sampleHash = ComputeSampleHash(stream, info.Length);
        return $"{info.Length:x}-{info.LastWriteTimeUtc.Ticks:x}-{sampleHash}";
    }

    /// <summary>
    /// Hashes up to three 64 KB chunks (start, middle, end) so files that differ only in the
    /// middle/end are still distinguished from a same-length, same-timestamp coincidence.
    /// </summary>
    private static string ComputeSampleHash(FileStream stream, long length)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[SampleChunkBytes];

        AppendChunk(stream, 0, buffer, hasher);

        if (length > SampleChunkBytes)
        {
            var middleOffset = Math.Max(0, length / 2 - SampleChunkBytes / 2);
            AppendChunk(stream, middleOffset, buffer, hasher);
        }

        if (length > SampleChunkBytes * 2)
        {
            var endOffset = Math.Max(0, length - SampleChunkBytes);
            AppendChunk(stream, endOffset, buffer, hasher);
        }

        return Convert.ToHexStringLower(hasher.GetHashAndReset());
    }

    private static void AppendChunk(FileStream stream, long offset, byte[] buffer, IncrementalHash hasher)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        var read = stream.Read(buffer, 0, buffer.Length);
        if (read > 0)
        {
            hasher.AppendData(buffer, 0, read);
        }
    }
}
