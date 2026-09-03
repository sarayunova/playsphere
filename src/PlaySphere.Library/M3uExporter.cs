using System.Text;
using Microsoft.Extensions.Logging;
using PlaySphere.Application.Library;

namespace PlaySphere.Library;

/// <summary>
/// Exports media file paths as a standard M3U playlist file (PICKLE.md §25). Emits an <c>#EXTM3U</c>
/// header and one <c>#EXTINF</c> + path line per entry. Best-effort: returns false (never throws) if
/// the destination cannot be written.
/// </summary>
public sealed class M3uExporter(ILogger<M3uExporter> logger) : IM3uExporter
{
    public Task<bool> ExportAsync(IReadOnlyList<string> mediaPaths, string destinationFilePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mediaPaths);
        ArgumentNullException.ThrowIfNull(destinationFilePath);

        try
        {
            var builder = new StringBuilder();
            builder.AppendLine("#EXTM3U");

            foreach (var path in mediaPaths)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(path)) continue;

                var title = Path.GetFileNameWithoutExtension(path);
                builder.Append("#EXTINF:-1,").AppendLine(EscapeTitle(title));
                builder.AppendLine(ResolvePathForExport(path));
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(destinationFilePath));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(destinationFilePath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return Task.FromResult(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            logger.LogWarning(ex, "Could not export M3U playlist to '{Path}'.", destinationFilePath);
            return Task.FromResult(false);
        }
    }

    private static string EscapeTitle(string title)
    {
        // Strip CR/LF so a crafted title cannot inject playlist lines.
        return (title ?? string.Empty).Replace('\r', ' ').Replace('\n', ' ');
    }

    private static string ResolvePathForExport(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }
}
