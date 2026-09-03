namespace PlaySphere.Domain.Media;

/// <summary>
/// A media item to play. Either a local file path (<see cref="FromPath"/>) or a remote
/// network stream URI (<see cref="FromUri"/>, e.g. http/https/rtsp/rtpm/rtp/ftp). Media
/// identity is never path-only, so remote sources are representable without a filesystem path.
/// </summary>
public sealed record MediaFile
{
    /// <summary>Absolute file path (when a local source) or the URI string (when a remote stream).</summary>
    public required string FilePath { get; init; }

    /// <summary>Human-friendly title for display without loading metadata.</summary>
    public string DisplayTitle { get; init; } = string.Empty;

    /// <summary>
    /// Non-null when this media is a remote network stream (not a local filesystem path).
    /// When set, <see cref="FilePath"/> holds the absolute URI string.
    /// </summary>
    public Uri? SourceUri { get; init; }

    /// <summary>True when this media is a remote network stream rather than a local file.</summary>
    public bool IsRemoteUri => SourceUri is not null;

    private static readonly HashSet<string> AllowedStreamSchemes = new(StringComparer.OrdinalIgnoreCase)
    {
        Uri.UriSchemeHttp, Uri.UriSchemeHttps, Uri.UriSchemeFtp,
        "rtsp", "rtmp", "rtmps", "rtmpt", "rtmpe", "mms", "mmsh", "udp", "rtp",
    };

    /// <summary>
    /// Creates a <see cref="MediaFile"/> from a local path, validating and canonicalizing it.
    /// </summary>
    /// <exception cref="ArgumentException">The path is null, empty or whitespace.</exception>
    public static MediaFile FromPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Media path must not be empty.", nameof(path));
        }

        return new MediaFile
        {
            FilePath = System.IO.Path.GetFullPath(path),
            DisplayTitle = System.IO.Path.GetFileNameWithoutExtension(path),
        };
    }

    /// <summary>
    /// Creates a <see cref="MediaFile"/> for a remote network stream URI (e.g. an http/https
    /// HLS/DASH/RTSP livestream or a direct media URL). The URI is validated to be absolute and
    /// use a scheme recognised as a network stream.
    /// </summary>
    /// <exception cref="ArgumentException">The URI is null/blank, not absolute, or uses a
    /// scheme libVLC cannot open as a network stream.</exception>
    public static MediaFile FromUri(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri))
        {
            throw new ArgumentException("Media URI must not be empty.", nameof(uri));
        }

        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
        {
            throw new ArgumentException($"Invalid media URI: '{uri}'", nameof(uri));
        }

        if (!AllowedStreamSchemes.Contains(parsed.Scheme))
        {
            throw new ArgumentException(
                $"Unsupported stream scheme '{parsed.Scheme}'. Use http(s), rtmp(s), rtsp, mms, udp or ftp.", nameof(uri));
        }

        return new MediaFile
        {
            FilePath = parsed.AbsoluteUri,
            SourceUri = parsed,
            DisplayTitle = DeriveTitleFromUri(parsed),
        };
    }

    /// <summary>
    /// Creates a <see cref="MediaFile"/> from a string that is either a local path or a remote
    /// stream URI, choosing the appropriate factory. Never throws: a value that is neither a
    /// path nor a recognised stream scheme is returned as a local path (the engine/UI surface
    /// the error later). This is the entry point for the "Open URL"/"Open File" input paths.
    /// </summary>
    public static MediaFile FromPathOrUri(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Media source must not be empty.", nameof(value));
        }

        // A genuine absolute stream URI (http/https/rtsp/rtmp/ftp/mms/udp/rtp) is treated as a
        // remote stream; everything else (absolute or relative local paths) goes down the path path.
        if (Uri.TryCreate(value, UriKind.Absolute, out var parsed) &&
            AllowedStreamSchemes.Contains(parsed.Scheme))
        {
            return FromUri(value);
        }

        return FromPath(value);
    }

    private static string DeriveTitleFromUri(Uri uri)
    {
        // Prefer a meaningful last path segment (e.g. file name) so the Now Playing bar, title
        // bar and cards show something legible instead of a raw long URL.
        var segments = uri.Segments;
        if (segments.Length > 0)
        {
            string last = segments[segments.Length - 1].TrimEnd('/');
            if (!string.IsNullOrEmpty(last))
            {
                int dot = last.LastIndexOf('.');
                if (dot > 0)
                {
                    string ext = last[(dot + 1)..];
                    if (ext.Length is > 0 and <= 12)
                    {
                        return last[..dot];
                    }
                }
                return last;
            }
        }

        return uri.Host;
    }
}