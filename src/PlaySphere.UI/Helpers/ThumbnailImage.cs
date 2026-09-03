using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace PlaySphere.UI.Helpers;

/// <summary>
/// Defensive helper that builds a thumbnail <see cref="Image"/> card element from a
/// <see cref="PlaySphere.Domain.Library.LibraryMediaItem.CoverArtPath"/> absolute file path.
/// The path is untrusted (loaded from the SQLite DB), so any failure — missing file, unreadable
/// content, bad URI — simply returns null and the card falls back to text-only rendering.
/// </summary>
public static class ThumbnailImage
{
    /// <summary>
    /// Creates a fixed-size, center-cropped <see cref="Image"/> backed by the local file at
    /// <paramref name="path"/>, or null when the file cannot be loaded. Never throws.
    /// </summary>
    public static Image? TryCreate(string path, int width, int height)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            return null;
        }

        try
        {
            var uri = new Uri(path);
            if (uri.Scheme != Uri.UriSchemeFile)
            {
                return null;
            }

            return new Image
            {
                Source = new BitmapImage(uri),
                Width = width,
                Height = height,
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
            };
        }
        catch
        {
            return null;
        }
    }
}
