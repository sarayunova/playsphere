using PlaySphere.Library;
using Xunit;

namespace PlaySphere.UnitTests;

/// <summary>Tests for the library file identity fingerprint (PICKLE.md §24). Pure file I/O — no engine.</summary>
public sealed class MediaFileFingerprinterTests : IDisposable
{
    private readonly string _directory;

    public MediaFileFingerprinterTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "PlaySphereFingerprintTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string WriteFile(string name, byte[] content)
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public void Compute_is_stable_for_the_same_unchanged_file()
    {
        var path = WriteFile("a.bin", [1, 2, 3, 4, 5]);

        var first = MediaFileFingerprinter.Compute(path);
        var second = MediaFileFingerprinter.Compute(path);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Compute_differs_for_different_content_of_the_same_length()
    {
        var pathA = WriteFile("a.bin", [1, 2, 3, 4, 5]);
        var pathB = WriteFile("b.bin", [5, 4, 3, 2, 1]);

        // Force identical timestamps so only content differentiates the fingerprint.
        var when = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(pathA, when);
        File.SetLastWriteTimeUtc(pathB, when);

        Assert.NotEqual(MediaFileFingerprinter.Compute(pathA), MediaFileFingerprinter.Compute(pathB));
    }

    [Fact]
    public void Compute_differs_for_different_length()
    {
        var pathA = WriteFile("a.bin", [1, 2, 3]);
        var pathB = WriteFile("b.bin", [1, 2, 3, 4]);

        Assert.NotEqual(MediaFileFingerprinter.Compute(pathA), MediaFileFingerprinter.Compute(pathB));
    }

    [Fact]
    public void Compute_handles_empty_file()
    {
        var path = WriteFile("empty.bin", []);

        var fingerprint = MediaFileFingerprinter.Compute(path);

        Assert.False(string.IsNullOrEmpty(fingerprint));
    }

    [Fact]
    public void Compute_handles_file_larger_than_two_sample_chunks()
    {
        // > 128 KB so all three sample regions (start/middle/end) are exercised.
        var content = new byte[200 * 1024];
        new Random(42).NextBytes(content);
        var path = WriteFile("large.bin", content);

        var fingerprint = MediaFileFingerprinter.Compute(path);

        Assert.False(string.IsNullOrEmpty(fingerprint));
    }

    [Fact]
    public void Compute_throws_FileNotFoundException_for_missing_file()
    {
        var missing = Path.Combine(_directory, "does-not-exist.bin");
        Assert.Throws<FileNotFoundException>(() => MediaFileFingerprinter.Compute(missing));
    }
}
