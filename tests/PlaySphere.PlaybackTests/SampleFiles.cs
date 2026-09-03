namespace PlaySphere.PlaybackTests;

/// <summary>Locates the generated legal-media corpus (see tools/generate-media-fixtures.ps1).</summary>
public static class SampleFiles
{
    private static string? _samplesRoot;

    public static string Resolve(string fileName)
    {
        _samplesRoot ??= FindSamplesRoot();
        var candidate = Path.Combine(_samplesRoot, fileName);
        if (!File.Exists(candidate))
        {
            throw new FileNotFoundException(
                $"Sample '{fileName}' not found. Run tools/generate-media-fixtures.ps1 first.", candidate);
        }

        return candidate;
    }

    private static string FindSamplesRoot()
    {
        // Test bin runs from tests/PlaySphere.PlaybackTests/bin/... — walk up to the repo root.
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && dir is not null; i++)
        {
            var probe = Path.Combine(dir, "samples", "legal-media");
            if (Directory.Exists(probe))
            {
                return probe;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException("Could not locate samples/legal-media (repo root).");
    }
}