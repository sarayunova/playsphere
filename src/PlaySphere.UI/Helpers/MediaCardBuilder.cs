using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PlaySphere.Domain.Library;
// PlaySphere.Application (the project) is a reachable namespace from this file's enclosing
// PlaySphere.* tree, so alias the WinUI Application type to a distinct name (see AGENTS.md rule).
using XamlApplication = Microsoft.UI.Xaml.Application;

namespace PlaySphere.UI.Helpers;

/// <summary>
/// Builds the vibrant poster-style media cards used by <c>HomePage</c> (Continue Watching /
/// Recently Played rows) and <c>LibraryPage</c> (media gallery). Keeps the two pages visually
/// consistent — cover art (or an accent-gradient placeholder when there is no thumbnail), a
/// two-line title, a technical metadata line, an optional continue-watching progress bar, and
/// wired to navigate to the player. All styling is driven by the shared PICKLE.md §6/§7 design
/// tokens so no colors are hard-coded beyond the tokens.
/// </summary>
public static class MediaCardBuilder
{
    private const char PlayGlyph = '\uE768'; // Segoe MDL2 "Play"

    /// <summary>
    /// Creates a clickable poster card for <paramref name="item"/> that invokes
    /// <paramref name="openAction"/> when clicked. <paramref name="width"/> fixes the card width
    /// so gallery rows align; <paramref name="showProgress"/> surfaces the continue-watching bar
    /// and a percentage chip over the artwork.
    /// </summary>
    public static Button CreatePoster(
        LibraryMediaItem item,
        double width,
        bool showProgress,
        Action<LibraryMediaItem> openAction)
    {
        var card = new Border
        {
            Style = (Style)XamlApplication.Current.Resources["SurfaceCardStyle"],
            Width = width,
            Padding = new Thickness(12),
        };

        var content = new StackPanel { Spacing = 8 };

        var artworkWidth = width - 2 * card.Padding.Left;
        var artworkHeight = artworkWidth * 9.0 / 16.0;
        content.Children.Add(CreateArtwork(item, artworkWidth, artworkHeight, showProgress));

        content.Children.Add(new TextBlock
        {
            Text = item.Title ?? System.IO.Path.GetFileName(item.FilePath),
            Style = (Style)XamlApplication.Current.Resources["CardTitleTextStyle"],
            TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
            MaxLines = 2,
            TextWrapping = TextWrapping.Wrap,
        });

        var technical = MediaCardText.FormatTechnicalLine(item);
        if (technical != "No metadata")
        {
            content.Children.Add(new TextBlock
            {
                Text = technical,
                Style = (Style)XamlApplication.Current.Resources["MutedTextStyle"],
                FontSize = 11,
                TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis,
                MaxLines = 1,
            });
        }

        card.Child = content;

        var button = new Button
        {
            Content = card,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Tag = item,
        };
        button.Click += (_, _) => openAction((LibraryMediaItem)button.Tag);
        return button;
    }

    /// <summary>
    /// Builds the rounded 16:9 artwork region: the thumbnail <see cref="Image"/> when a cover-art
    /// file exists, otherwise an accent-gradient placeholder with a centered play glyph. An
    /// optional continue-watching bar and percentage chip overlay the bottom edge over a scrim.
    /// A <see cref="Border"/> with <see cref="Border.CornerRadius"/> clips everything to the
    /// artwork's rounded corners.
    /// </summary>
    private static FrameworkElement CreateArtwork(
        LibraryMediaItem item,
        double width,
        double height,
        bool showProgress)
    {
        var frame = new Border
        {
            Width = width,
            Height = height,
            CornerRadius = new CornerRadius(8),
        };

        var layer = new Grid();

        var backdrop = new Border
        {
            Background = (Brush)XamlApplication.Current.Resources["GradientAccentPrimary"],
        };

        var thumb = ThumbnailImage.TryCreate(item.CoverArtPath ?? string.Empty, (int)width, (int)height);
        if (thumb is not null)
        {
            backdrop.Child = thumb;
        }
        else
        {
            backdrop.Child = new FontIcon
            {
                Glyph = PlayGlyph.ToString(),
                FontSize = 30,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(140, 255, 255, 255)),
            };
        }

        layer.Children.Add(backdrop);

        if (showProgress && item.WatchedPercentage is > 0 and <= 98)
        {
            var overlay = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Spacing = 2,
                Padding = new Thickness(8, 6, 8, 6),
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(120, 0, 0, 0)),
            };
            overlay.Children.Add(new TextBlock
            {
                Text = $"{item.WatchedPercentage:N0}%",
                FontSize = 11,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                HorizontalAlignment = HorizontalAlignment.Right,
            });
            overlay.Children.Add(new ProgressBar
            {
                Value = item.WatchedPercentage,
                Minimum = 0,
                Maximum = 100,
                Height = 4,
            });
            layer.Children.Add(overlay);
        }

        frame.Child = layer;
        return frame;
    }
}
